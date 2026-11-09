# Vanilla scenario captures

The redstone, fluid, daylight detector and sulfur parity tests replay scenarios recorded on a headless vanilla
server. Each fixture in `Obsidian.Tests/Assets` is its own input: it holds every scenario's definition, and a capture
replaces only what vanilla produced.

- `redstone_scenarios.json` (`VanillaRedstone`) defines each scenario's box, setup, actions, reloads, entity contacts
  and arrows; the capture records a checkpoint per tick and each reload's `ticks`. A setup placement's `items` (slot,
  id and count) go into its block entity, as the `/setblock`'s `Items` data. Scenarios marked `level` need block
  entities (containers and hoppers), so the replay runs them on one shared test server and reuses loaded chunks,
  with a player beside each disjoint box, instead of on the redstone test world; they can't use reloads, entities
  or arrows. `observer_watches_state_changes` also uses the whole level so both update engines deliver shape
  work for the same changes; its recorded pulses guard scheduled-tick deduplication. The chest obstruction cases
  retain vanilla's stale comparator output when a solid block is placed
  above the chest and removed, including above either half of a double chest, without moving items.
  The `piston_*observer` / `piston_observer_*` and `sticky_piston_pull_idle_observer` cases record pulses after a
  pushed or pulled observer finishes moving, both watching unchanged air and ending beside pre-existing stone.
  Movement finishes at tick 10 and the pulse runs at ticks 12–13; sticky retraction finishes at 22 and pulses at
  24–25. `piston_fence_derives_connections` records a moved fence deriving its destination connections at tick 10,
  so the pre-placement derivation is shared by all moved states, rather than special-casing observers.
- `fluid_scenarios.json` (`VanillaFluids`) defines the box, setup fills, actions, dimension and checkpoint ticks; the
  capture records the checkpoints.
- `daylight_detector.json` (`DaylightDetectorTests`) defines the clock times to sample; the capture records the
  readings.
- `sulfur_scenarios.json` (`VanillaSulfurBlocks`) defines setup, actions, watched positions and sampling intervals.
  It records full block states and potent sulfur countdowns at absolute game times, including elapsed ticks across
  save/restart actions. Its world seed is `test-seed`. Cases with `recordEntities` also sample live falling blocks,
  damaging falling blocks and total item counts (`item` selects the item; sulfur spikes by default).
  Extra `sampleTicks` check the interval before landing
  as well as the completed fall, so immediate disappearance, missing drops and endlessly falling entities fail.
  `recordProjectile` samples embedded projectiles; projectile actions exercise collision handling through the live
  entity tick. RCON samples do not record sound effects.
  Projectile actions can supply `inBlockState` to load an embedded projectile in vanilla's format. The `*_release_*`
  references cover release into short grass, retention near a solid replacement, and the saved-state equality guard
  when the current block has no collision. Each case covers arrows, spectral arrows and tridents.
  `noGravity: false` enables gravity for a projectile action. With `recordProjectileMotion`, checkpoints record all
  three position and velocity components plus the saved `life` counter. The `*_release_gravity` references compare
  these values exactly on the release tick and subsequent flight ticks.
  `spawnFalling` supplies a block, position and motion; `recordFallingMotion` records its height, vertical velocity
  and grounded flag. The sand references cover a full floor, a partial floor and waiting for a moving piston to
  clear. `withoutBlockEntity` validates the absence of a block entity after vanilla's bare moving-piston placement;
  the replay removes Obsidian's automatically created default entity to reproduce that fixture's block view.
  The dripstone column references cover shape changes on adding and removing tips in both vertical directions.
- `sulfur_fixtures.json` (also `VanillaSulfurBlocks`) holds bounded Java probes for sulfur placement, growth and
  subsequent random draws, gas source/visibility, geyser force and fall-distance handling, trident thresholds and
  raw falling-spike damage. `Java/SulfurFixtures.java`, registered in `FixtureDumper.cs`, invokes vanilla's routines
  against each case's block view. Growth writes are recorded without neighbor notifications; the RCON scenarios
  separately cover the live update path. The impact target records the raw damage passed by vanilla's falling
  entity, before armor and other victim-specific mitigation.
  The `review_landing_*` probes instead supply a resolved ground contact and invoke vanilla's falling entity tick;
  they record removal, item drops and level events for every combination of `entity_drops`, `DropItem`, `CancelDrop`
  and `Silent`. The replay checks both item totals and the clients' landing-effect packets.
  The `shape_*` probes record vanilla's returned shape and scheduled water ticks for wet and dry panes, bars,
  leaves, sulfur spikes and pointed dripstone. Each reference is replayed through both the notification pipeline
  and the fluid shape-update path, including cases where a registered behavior returns an unchanged state.
  The `arrow_saved_state_*` probes record the `BlockState.CODEC` NBT representation used by vanilla's arrow saves.
  `ProjectilePersistence.VanillaBlockStateSurvivesRoundTripAndReleases` checks that the captured `inBlockState`
  survives loading and saving before the block changes. Default states are strings in 26.3; non-default states are
  `id`/`properties` compounds. These checks require the saved field, independently of collision-based release.
  The `arrow_release_*` Java probes load through vanilla's `TagValueInput` and run `AbstractArrow.tick` with gravity
  enabled in an empty block view. They record decoded states (including decode failure versus valid air), position,
  velocity, embedded state and `life`. The entity's random is seeded after construction/loading; its first three
  floats and next integer are captured. `ProjectilePersistence.ReplayVanillaReleaseTransition` supplies the same
  seed and checks exact vectors and the next draw for all three projectile types. The probe suppresses weather and
  unrelated block-inside effects; it retains vanilla's release transition and nearby collision query.

- `copper_weathering.json` (`VanillaCopperWeathering.ReplayWeatheringReference`) records bounded random ticks from
  `Java/CopperWeatheringFixtures.java`. Capture it with `-p:CaptureFixtures=copper_weathering`. All 13 weathering
  owners and 15 block families cover fresh, exposed, weathered, oxidized and waxed states, exact seeded draws and
  the next integer, neighborhood age effects, Manhattan scan boundaries, and connected doors/chests. The view runs
  native shape updates for connected halves and records block entity types and their native state compatibility;
  chunk lifecycle side effects are outside the probe. The replay uses the generated owner dispatch and live block
  mutation path, checking states, random consumption and resident block entity identity in one test.

- `more_blocks.json` (`VanillaMoreBlocks.ReplayConstructionAndMusicReference`) holds bounded 26.3 construction
  and music probes from `Java/MoreBlockFixtures.java`. Placement runs the chosen block's placement routine and
  survival check; signs are already a standing or ceiling variant. It records full states for every wool/concrete
  stair/slab color and poplar construction families, including powered placement, waterlogging and stair corners.
  Shape cases record scheduled fluid/block ticks and replay through live notifications. Leaf-distance ticks and
  axe providers also capture the next random draw; stripping checks state, sound ID and durability cost. Door,
  trapdoor and gate uses check state and sound ID (their captured pitch is not asserted by this replay); door
  placement checks the upper half. Note cases invoke vanilla's note and block-event routines, capture trigger
  success, sound ID/volume/pitch/seed and the next random draw, and replay sound and block-event packets. A single
  test uses `FixtureCases.AssertAllAsync` over the entire file and reports all case-labelled failures once.
  Review lifecycle cases call the real `ServerLevel.blockEvent` and `runBlockEvents` routines. They record pending
  event counts, pre-phase silence, final playback and packets, exact seeds and the next draw: duplicate activation,
  cancellation on replacement, restoration, state/instrument changes, distinct positions and later-phase playback.
  Their replay advances the real Obsidian world tick, checking the block-event phase is connected. The use-item
  cases call `ServerPlayerGameMode.useItemOn` with all seven top-instrument heads and stone controls on upward and
  side faces, plus a sneaking control; they check note counters, target block states/rotations and consumption
  through Obsidian's `UseItemOnPacket`. The `shared_*` block-event cases use the native insertion-ordered event
  set and drain with piston movement and note playback together. They record pre/post queue sizes, ordered sound
  and event packets (including block types), and watched block states. Opt-in piston notifications invoke native
  note neighbor reactions, so a note queued during the drain must run in that phase. They cover both note/piston
  orders, duplicates, every key component, replacement, inactive-chunk retry, lids (including copper chests and
  colored shulkers), bells, gateways, spawners and geysers. Gateway cooldown and spawner delay-reset cases also
  invoke their native producers. Mixed cases check sound IDs/order and movement; exact piston pitch/random draws
  are outside this queue change, while the existing note cases retain their exact seed/pitch assertions. The bounded
  level suppresses advancement/stat delivery and unrelated neighbor updates. These are native routine captures,
  rather than an RCON interaction or sleep lifecycle capture.

  The `review_block_event_ticking_ranges` case records `SimulationChunkTracker` and `ChunkLevel` results for player,
  ticking-ticket and loaded-only sources, including the block/entity boundary and diagonal corners. Player ticket
  levels come from `DistanceManager.getPlayerTicketLevel`. Its replay checks the predicate outside the tick and
  dispatches real events through the tick's player snapshot. `review_piston_block_ticking_fringe` and
  `review_piston_outside_block_ticking` run the native event drain against that tracker: the first extends a piston
  in chunk (6,0) powered from (5,0), while (7,0) retains its pending event. All three remain in the same reference
  and `VanillaMoreBlocks.ReplayConstructionAndMusicReference` test. The same range replay now schedules block ticks
  with all eight neighbors ready and checks their dispatch against the captured block-ticking results.
  `review_container_periodic_opener_recheck` invokes the native chest's `ContainerOpenersCounter.recheckOpeners`
  with a stale count and no viewers, recording its closing event and five-tick interval. The replay loses a viewer
  without notifying the window lifecycle, then checks silence before the per-container deadline and repair at it.
  The two `observer_player_faces_*` cases instead run `PlacementFixtures.java` on a real dedicated server's level
  through `ServerPlayerGameMode.useItemOn`. Both facing air and facing stone, placement consumes one item and
  schedules no observer tick. Their replay drives `UseItemOnPacket` and checks the state, scheduled tick and hand.
  Command placement's initial pulse remains covered by `redstone_scenarios.json`: command block inputs derive
  their state with shape updates before placement, unlike block items.

- `structure_pieces.json` (`VanillaStructureSaves.StartsSaveLikeVanilla`) holds what vanilla 26.3 saves of
  structure starts (`StructureStart.createTag`), from `Java/StructurePieceFixtures.java`, which runs a dedicated
  server of its own in a new world (the file's `seed`, structures on) under the scratch directory. A case names a
  start of the worldgen `structures` capture (structure, dimension, start chunk) and records `fresh`, its save as
  generated, and with `place`, `placed`, its save once every chunk its box reaches is decorated, in x then z order:
  what placing moved and flagged (heights, chests, spawners, a spider's spawner). When vanilla loading its last save
  and saving it again changes it (it recomputes igloo and terrain matching jigsaw boxes), that is `reloaded`. Each
  save is compact SNBT, the start without `Children`, then one string per piece. The replay generates each start
  through the 26.3 pipeline and checks that Obsidian saves it as vanilla did, accepts each of vanilla's saves of it,
  and restores vanilla's last save and saves that back. For placed starts it also decorates every chunk the start
  reaches in the capture's order, in one world, and checks that Obsidian then saves what vanilla saved.
  The cases are one start of each structure type, 28 in all, which reach every piece type Obsidian generates except
  the ocean monument's rooms, which vanilla doesn't save (it saves the building and regenerates its rooms).

- `heightmaps.json` (`RuntimeHeightmaps.ReplayVanillaRuntimeHeightmapReference`) records native 26.3
  `LevelChunk.setBlockState` edits on empty chunks, with overworld and nether dimension types from a dedicated
  server. Each checkpoint records the edited column's first free Y (`getHeight + 1`) and all packed longs for
  the four runtime maps. Cases place, replace and remove solids, plants, snow, fluids and wet/dry leaves, cross
  section and packed-word boundaries, and touch both build-height limits. The same reference calls every
  `Heightmap.Types.isOpaque` predicate on every vanilla block state, including the two generation types.
  The replay checks heights, exact packed storage and the chunk packet's client subset in one test through
  `FixtureCases.AssertAll`. Separate Obsidian tests cover persistence, restoring absent maps from older saves,
  and concurrent column writes and snapshots. The probe edits isolated native chunks on the server thread;
  neighboring-block notifications and world generation do not alter these inputs.

- `mining_progress.json` (`VanillaMining.ReplayDestroyProgressReference`) records vanilla's
  `BlockState.getDestroyProgress` for a survival player, from `Java/MiningFixtures.java` (`CaptureFixtures=mining`).
  Each case names a block's default state, the held item and helmet with their enchantments, effect amplifiers,
  whether the player stands on the ground and whether their eyes are in water. The probe applies the equipment's
  enchantment attribute modifiers the way vanilla does (`EnchantmentHelper.forEachModifier`), so Efficiency and Aqua
  Affinity come out of vanilla's own data. The replay checks Obsidian's progress exactly, then starts mining the
  block through `PlayerActionPacket` and checks that it breaks at once exactly when the progress is 1 or more:
  clients break those blocks themselves and never send a finish. Infinite progress (no hardness) is the string
  `"Infinity"`, which JSON numbers can't hold.

- `block_placement.json` (`VanillaBlockPlacement.ReplayBlockItemPlacementReference`) records what every vanilla
  block item places, from `Java/PlacementFixtures.java`. The probe runs a dedicated server of its own in a flat world of
  air and uses each item (registry order, those enabled by default) through `ServerPlayerGameMode.useItemOn`, the
  routine behind a use-item-on packet, with a connectionless survival player whose head turns with it. The file's
  `contexts` are its input: blocks set around a target without updates, the clicked block, face and cursor, and the
  player's yaw, pitch and sneaking. They cover floors of stone, grass and farmland at three yaws and pitches, a wall
  clicked high, low and from the east, a ceiling with and without sneaking, and a water source. A context with `items`
  runs only for those (a bell between two walls; chests beside an oxidized copper chest) and records `null` for the
  rest. Each item records one result per context: every block within two of the target that changed, so doors' upper halves, beds' heads and tall
  plants are included. A state lists only the properties that differ from its default, which keeps the 10,276 results
  to about 500 KB. The replay shares one world and player, clears the area between cases and drives
  `UseItemOnPacket`. It compares kelp and vines without their random age (see
  [block item placement](known-gaps/block-item-placement.md)).

Each file's `_notes` describes exactly how it was recorded, and `version` names the vanilla version; the tests check it
against the version Obsidian implements.

`VanillaSulfurBlocks.ReplayCapturedScenarioFixture` and `ReplayCapturedProbeFixture` each report one test for
their entire fixture. The probe replay includes projectile state and seeded release cases for all three projectile
types, plus both shape-update paths. Each replay collects case-labelled failures and asserts once after all cases
have run. Obsidian-only countdown, packet and projectile save/load checks use three small tables, each in one test.

## Re-recording

```sh
dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios
dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=redstone+daylight

dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=sulfur

dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=more_blocks

dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=structure_pieces

dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=heightmaps

dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=placement

dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=sulfur -p:CaptureCases=spike_shatters_after_falling+review_spike_pending_fall_keeps_frustum
```

This uses the server of the Minecraft version `Obsidian.csproj` targets, from the shared vanilla cache (see
[vanilla captures](vanilla-captures.md#the-vanilla-cache)), and needs Java 25. The sulfur capture takes about 30 seconds.
To add a scenario, add its definition with placeholder checkpoints (the redstone capture records as many ticks as the
scenario has checkpoints) and capture again; the tests pick it up.

For redstone, `CaptureCases` accepts exact `+`-separated scenario names and records only those; the other
scenarios keep what they hold.

For sulfur, `CaptureCases` accepts exact scenario names separated by `+`. It replaces only those cases' checkpoints
and skips the Java probes. Other scenarios retain their stored absolute times and are replayed independently.
`SulfurFixtures.java` also accepts an optional second argument of exact `+`-separated probe names, when run directly
with the vanilla server and libraries on the classpath, to re-record only new or changed Java cases.

For `more_blocks`, `CaptureCases` accepts exact `+`-separated names in `more_blocks.json` and re-records only those
Java probes. No RCON server is started and no other fixture is changed. Its source and the reusable runtime
instrument dumper are separate so editing these probes does not regenerate unrelated assets.

The `placement` capture takes about 10 seconds after the server starts and re-records every item; to add a context,
add it to `contexts` and capture again.

A `structure_pieces` case with `captureReferences: true` also records `referenceChunks`: the saved
`structures.References` from vanilla's chunk serializer at STRUCTURE_REFERENCES, across its bounding box plus
one chunk of border. `VanillaStructureSaves.GeneratedChunksSaveVanillaReferences` compares generated chunk saves
with those compounds, including empty border chunks and packed start positions at negative coordinates.

For `structure_pieces`, `CaptureCases` accepts exact `+`-separated case names and re-records only those starts. The
capture takes about 15 seconds after the server starts. `StructurePieceFixtures.java` is an asset input like the
other dumpers, so editing it regenerates the assets once.

The underlying command is `Obsidian.AssetGenerator capture-scenarios <minecraft version> <cache directory>
<scratch directory> <test assets directory> [redstone+fluids+daylight+sulfur+more_blocks+structure_pieces+placement]
[case names]`.

## How it captures

`ScenarioCaptures.cs` drives the server over RCON (`ScenarioServer.cs`):

- **The world** is a void superflat, and a data pack makes the nether one too while keeping its dimension type (fast
  lava). Random ticks, mob spawning and weather are off. Each scenario's chunks are force-loaded, so they keep ticking
  and load before the first tick after a restart.
- **Time** is frozen by the data pack's load function, which runs in the first tick after every start, so a start runs
  exactly one game tick. Each scenario tick is one `/tick step 1`, waited on until the game time moves.
- **Blocks** are placed with `/setblock` (live, with updates) or `/fill … strict` (fluid setup, without updates), and
  read with `execute if block`: first the block's previous state, then its id among likely ones, then each property's
  values in turn. Palettes hold full states with every property, sorted by name.
- **Reloads** save the world, stop the server and start it again; the captured `ticks` is how far the game time moved.
- **Entities** are armor stands standing in each contact cell from their event to the next one, and **arrows** fly
  straight into the face at the given location without gravity, so they cross it exactly there.
- **Daylight readings** set the overworld clock to each sample, at each phase of the update interval in turn, and
  step to the next game time that is a multiple of 20, when detectors update; vanilla ticks the clocks before the
  levels, so the clock's time then is the time the detectors saw.

Between scenarios, the area is cleared without updates and the game runs a few ticks, so that work a scenario left
scheduled finds air and is dropped.
