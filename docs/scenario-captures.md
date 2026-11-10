# Vanilla scenario captures

The redstone, fluid, daylight detector and sulfur parity tests replay scenarios recorded on a headless vanilla
server. Each fixture in `Obsidian.Tests/Assets` is its own input: it holds every scenario's definition, and a capture
replaces only what vanilla produced.

- `redstone_scenarios.json` (`VanillaRedstone`) defines each scenario's box, setup, actions, reloads, entity contacts
  and arrows; the capture records a checkpoint per tick and each reload's `ticks`. A setup placement's `items` (slot,
  id and count) go into its block entity, as the `/setblock`'s `Items` data. Scenarios marked `level` need block
  entities (containers and hoppers), so the replay runs them on one shared test server and reuses loaded chunks,
  with a player beside each disjoint box, instead of on the redstone test world; they can't use reloads, entities
  or arrows. `observer_watches_state_changes` also uses the whole level, where the fluid level's updater delivers
  shape work and the notification pipeline the neighbour reactions; its recorded pulses guard scheduled-tick
  deduplication. The chest obstruction cases
  retain vanilla's stale comparator output when a solid block is placed
  above the chest and removed, including above either half of a double chest, without moving items.
  The `piston_*observer` / `piston_observer_*` and `sticky_piston_pull_idle_observer` cases record pulses after a
  pushed or pulled observer finishes moving, both watching unchanged air and ending beside pre-existing stone.
  Movement finishes at tick 10 and the pulse runs at ticks 12–13; sticky retraction finishes at 22 and pulses at
  24–25. `piston_fence_derives_connections` records a moved fence deriving its destination connections at tick 10,
  so the pre-placement derivation is shared by all moved states, rather than special-casing observers. Its setup
  gives the fence `west=true`, the connection vanilla's `/setblock` derives beside the piston, because Obsidian's
  commands don't derive placed states; recording again with the property given left every checkpoint unchanged.
  `piston_pushes_carpet_off_support` pushes a carpet over air: the finished move breaks it at tick 6 and one carpet
  drops. A scenario naming an `item` also records in each checkpoint the total count of that item lying as item
  entities within two blocks of its box, and the replay counts Obsidian's the same way, so it must be `level`.
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
  Shape cases record scheduled fluid/block ticks and replay twice: through the fluid level's shape update, which a
  live level runs, and through the notification pipeline, which runs them where it's the only engine; where
  vanilla's shape update breaks the block, the replay runs one level tick, which is when Obsidian removes it. Each
  shape case also runs through the shared shape pass as generation reads it, wherever that pass has a rule for the
  block, so the fence, stair and survival cases cover generation's rules too. The grindstone, wall button and nether
  wart cases check blocks that gained a shape rule when rules were bound by declaring class. Leaf-distance ticks and
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
  Command placement's initial pulse remains covered by `redstone_scenarios.json`. Vanilla command inputs derive
  their state from all six neighbours before placement, unlike block items. Obsidian's `/setblock` and `/fill`
  only give a placed observer its shape reaction; they do not generally derive command inputs' states.

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

- `fire_ticks.json` (`VanillaFire.ReplayFireReference`) records vanilla's fire from `Java/FireFixtures.java`
  (`CaptureFixtures=fire`): `FireBlock.tick` on the block at the origin, or a block set there with `Level.setBlock`
  so that its `onPlace` runs, on a bounded block view that runs what `LevelChunk.setBlockState` and `Level.setBlock`
  run (block entity removal, `onPlace`, neighbour and shape updates). A case lists blocks relative to the origin, the
  rain, difficulty, dimension type and biome, and the seeds to run it with; each run records every draw from the
  level's random in order, the next int, the final state of each block it touched, the block ticks scheduled and the
  entities added. The cases cover ageing, burning out with and without a sturdy floor or fuel, fire that can't
  survive (vanilla carries on with the tick after putting it out, and ageing it places it again), spreading to wool
  in every direction and into the floor, igniting the air around hay, rain with and without a roof, netherrack,
  magma and the End's bedrock, jungle's faster burnout, soul fire spreading over soul soil, TNT and a lectern with a
  book burning, placing fire and soul fire, and placing fire in a nether portal frame: in the lower and the upper
  interior cell, along either axis, in an incomplete frame, and in the nether and the End (vanilla lights portals
  from fire in the overworld and the nether only, before checking that the fire survives). The replay runs each
  seed at its own spot in a level of the case's dimension (the overworld, or a nether or End dimension of the test
  world), with a level random that records its draws, and places fire through `AbstractLevel.SetBlockAsync`, the
  path fireballs, explosions and end crystals use. It compares blocks and dropped items once the levels have
  ticked, as Obsidian removes fire a shape update destroys, drops a removed block entity's items, and lights a
  portal around new fire later in that tick.
- `ticking_blocks.json` (`VanillaTickingBlocks.ReplayTickingBlockReference`) records single block routines from
  `Java/TickingBlockFixtures.java`, against a bounded block view of the case's setup with a seeded, recording random
  as both the routine's and the level's: a random tick (only of a state that random-ticks), a scheduled tick, a
  `Level.setBlock` with its on-place and neighbour shape updates (not neighbour notifications), or a falling block's
  landing. Each case records the draws, block writes, scheduled block and fluid ticks with their delays, level events,
  game events, the entities added (a falling block's state and stalactite damage) and the blocks destroyed. The cases
  cover pointed dripstone growing (the block and water source above, column length, stalagmite search), dripping
  water and lava into each kind of cauldron or drying mud into clay, a waterlogged block counting as water (above
  the column, ahead of the tip and as a floor), falling and breaking when the support goes,
  thickness along a column, landing, and cauldrons taking the drip; budding amethyst starting a bud on each face and
  growing each stage, into air, water sources and falling water, and the faces it skips; lit redstone ore going dark;
  nylium under blocks that do and don't let light down; dirt paths turning to dirt, and which new blocks above or
  beside them schedule that; coral blocks, plants, fans and wall fans dying dry or staying by water, scheduling their
  death when placed or when a neighbour changes, and breaking when their support goes; chorus plants staying, breaking
  and connecting as their support and neighbours change; an oak door whose stone floor becomes soul sand (it stays)
  or oak leaves (both halves break). The replay runs each case through Obsidian's generated
  owners, or a live block change, in its own world, and compares everything but the game events. Obsidian destroys
  blocks in the tick's entity phase, so a case that destroys one runs the world tick before comparing, keeping the
  recording random active. Ordered writes compare every flag bit; destruction compares state and drops intent;
  falling entities compare type, position, state, damage enablement, damage per distance and maximum damage. The
  replay rejects unexpected entities and writes outside the case and scans the loaded chunks for unexpected blocks.
  Explicit expected differences pin the resource-spawn draws of the two stalagmite cases and the door on leaves (the
  bounded probe suppresses loot), and the door's upper half breaking in the next entity phase, where vanilla breaks
  the chain at once (the replay runs that phase alone, without block ticks); each fails when it disappears. The
  `review_*` cases also cover mud in the nether, mixed sulfur/dripstone columns (growth and native `findTip`,
  including opposite-direction tips), and independent drops/silence landings.
- `eyeblossom_ghast_portal_ticks.json` (`VanillaEyeblossomGhastPortalTicks.ReplayEyeblossomGhastPortalReference`)
  records eyeblossoms, potted eyeblossoms, dried ghasts and nether portals from
  `Java/EyeblossomGhastPortalFixtures.java` (`CaptureFixtures=eyeblossom_ghast_portal`): a random tick, a scheduled
  tick or a `Level.setBlock` against a bounded block view of the case's setup, with a seeded, recording random as both
  the routine's and the level's. A case can also give block ticks already `pending`, the `dimension` (whose attributes
  and timelines the view samples with every clock at `dayTime`), the `difficulty`, the `spawnMobs` and `spawnMonsters`
  rules and whether a player is near (`playerNear`). Each case records the draws, the ordered writes with their flags,
  scheduled block and fluid ticks with their delays, level events, game events, sounds (position or followed entity,
  source, volume and pitch, which is null where a mob's own random draws it), particles (a trail's target, colour and
  duration) and the entities added (type, position, rotations, portal cooldown, age, and a mob's baby flag, main hand,
  enchantment, handedness, loot pickup and vehicle). The view reads the eyeblossom's neighbour scan through chunk
  sections, so it keeps vanilla's section order. The cases cover eyeblossoms turning by night and by day on random and
  scheduled ticks, at the keyframes either side of dusk and dawn, on a later day, never in the nether or the End, and
  the wave of neighbours they schedule across chunk and section borders (out of range, other states and pots skipped);
  potted eyeblossoms turning, staying, and other potted plants not ticking; dried ghasts asking for their tick (wet,
  drying, already pending, pending elsewhere), hydrating, drying, becoming a ghastling in each facing and beside other
  blocks, and placed dry or in water; portals spawning a zombified piglin on each difficulty (draws 0, 1 and 2 against
  easy, normal and hard), not drawing at all when peaceful, in the nether or the End or without either spawning rule,
  drawing but not spawning without a player near or over air, glowstone, glass or a bottom slab, spawning over magma,
  as a baby, as a baby looking for a chicken to ride, on a new chicken jockey, with a golden spear, with an enchanted
  weapon, left-handed and from the portal's bottom block, and on Halloween (with a carved pumpkin, a jack o'lantern or
  neither) and the days either side of it. Eyeblossom waves also cross a section border with flowers on both sides, at
  y 63/64 and at -17/-16. The level holds vanilla's registries with their tags and is plains everywhere. A case's
  `date` is the local date vanilla sees (the fixture's `capturedOn` otherwise): the probe can't set the JVM's clock,
  so it moves vanilla's `SpecialDates.HALLOWEEN` constant by as many days as the date is from the real one, and
  vanilla's own comparison runs unchanged; the replay gives the level a clock on that date. The replay runs each case
  through Obsidian's generated owners, or a live block change, in its own world with a listener beside the origin (or,
  without `playerNear`, more than 128 blocks away), and compares all of it: draws, writes with every flag bit, blocks
  (and none outside the case), pending ticks, level events, the sound and particle packets the listener gets, game
  events, entities in any order (rotations as clients get them, in 256ths of a turn), their add order when more than
  one is spawned (through the listener's add packets), and destroyed blocks. Optional `chickens` supplies existing
  chickens in query order, each with `pos` and optional `alive: false`, `riding: true` (in an oak boat) or
  `passenger: true` (an armor stand). `chickensAfter` records their jockey flags, portal cooldowns and passengers;
  setup entities are excluded from the spawned-entity comparison. Six cases cover riding an eligible chicken,
  each exclusion, skipping an ineligible first candidate and choosing the first eligible rather than the nearest,
  and a failed first jockey roll followed by a successful second roll. A successful first roll with no eligible
  chicken never tries the second roll. Equipment drop chances are not captured. Where
  vanilla records a mob voice's pitch as null, Obsidian's must lie in the voice's range, and where it records a jockey
  chicken's head turn as null, Obsidian's must lie in vanilla's 0 to 2pi degrees. Three cases are pinned as expected
  differences, each failing when it disappears: over glowstone, glass and a bottom slab Obsidian's spawner floor rule
  lets the piglin spawn, so they must match the obsidian-floor case with the same seed (see [the known
  gaps](known-gaps/eyeblossoms-dried-ghasts-and-portal-piglins.md)).
  An optional `dimension` supplies that dimension type's environment attributes; optional `actions` are subsequent
  `UPDATE_ALL` writes. `findTip` takes `maxLength` and `includeMerged` and records the returned position or null.

- `block_use.json` (`VanillaBlockUse.ReplayBlockUseReference`) records what using a block does, from
  `Java/BlockUseFixtures.java`. Like the placement probe it runs a dedicated server of its own in a flat world of air,
  and each case uses its main hand's item through `ServerPlayerGameMode.useItemOn`; the area's chunks are forced, so
  block events run as a tick runs them. A case names its blocks, the clicked face, the item and count, the off hand,
  the game mode and sneaking. It records the result (which decides the swing), the blocks that changed, the hand,
  the entities that appeared, the sounds, level events and block events a second player beside the target received,
  the game events within 16 blocks (with their source), the statistics awarded and the menu opened, and when a menu
  opened, what closing it changed and played. The replay drives `UseItemOnPacket` in one world, with full sky light
  as the capture's open sky has, runs the queued entity actions and the tick's block events, and checks swings from
  the result. The 92 cases cover the dispatcher, sign applications and editing, advancement context, use swing charge and
  container close sources; mob ambient sounds aren't compared (see
  [mob ambient sounds](known-gaps/mob-ambient-sounds.md)).

- `turtle_eggs.json` (`VanillaTurtleEggs.ReplayTurtleEggReference`) records vanilla's turtle eggs, from
  `Java/HatchingFixtures.java` (`CaptureFixtures=turtle_eggs`). Each case is its input: the blocks of a small block
  view (air elsewhere), the origin, a seed, the overworld clock's time and the dimension, and either a random tick or
  a state to `place` with every update. The probe builds the level's environment attributes from the dimension
  type's own attributes and its timelines at that time (no biome or weather layers), uses a legacy random of the seed
  as both the tick's and the level's random, and runs each write's `onPlace` as a chunk does, then its neighbour
  notifications (with `UPDATE_NEIGHBORS`) and shape updates. It records the draws, the writes, the ticks scheduled, the sounds (without their seed), level events, game events, the entities added
  (position, yaw, age, home) and the final blocks. The cases cover cracking on each block of `#sand`, hatching one and
  four eggs, eggs on dirt and sandstone, the hatch window's edges, a later day, chance cracks outside the window and
  in the nether, and placing on and off sand. `chances` samples `gameplay/turtle_egg_hatch_chance` by dimension and
  time. The replay runs every case in one world, moved along x, through the random-tick dispatch or a block change,
  and compares everything recorded but the game events, which no client sees; the writes in order with every flag,
  as the level stores them (`LevelFluids.BlockStored`).

- `frogspawn.json` (`VanillaFrogspawn.ReplayFrogspawnReference`) records vanilla's frogspawn, from the same probe
  (`CaptureFixtures=frogspawn`). A case either places frogspawn with every update, which schedules its hatch with a
  draw of the level's random, or runs its scheduled tick (`tick`), which breaks it, then on a water source (or a
  waterlogged block) plays its sound and hatches two to five tadpoles at drawn offsets and yaws. The cases cover
  placing on water, stone and a waterlogged slab, hatching each number of tadpoles, and dying on flowing and falling
  water, stone and ice. The replay places through a block change and ticks through the level's scheduled-tick
  dispatch, in one world, and compares the draws, the writes in order with their flags, blocks, ticks, sounds, level
  events and tadpoles.

- `growing_plant_ticks.json` (`VanillaGrowingPlantTicks.ReplayGrowingPlantTickReference`) records vanilla's
  `randomTick` of growing plants, and the shape updates of kelp and the vines, from
  `Java/GrowingPlantTickFixtures.java` (`CaptureFixtures=growing_plant_ticks`). Each case is its input: the blocks of
  a small block view (air elsewhere), an optional ticked origin, optional actions after the tick (a state set with
  `UPDATE_ALL`, or a block removed), a seed, and optionally the raw light everywhere (`light`) and the `spread_vines`
  rule (`spreadVines`). The probe ticks the origin only if its state randomly ticks, with a legacy random of the seed
  as both the tick's and the level's random. It records whether the state ticks, every draw with its bound and result
  (a body turning back into a head draws its age), the ordered writes with their flags (neighbour shape updates run,
  so an old head turning into its body is one), the block and fluid ticks scheduled with their delays, and the final
  state of every position it set up or wrote. The cases cover kelp and the cave, weeping and twisting vines, melon and
  pumpkin stems, wall vines and the pitcher crop: growing, a missed draw, being blocked, the last age, light, the
  build height limits, each way a vine spreads, a tip broken off after growing, a head growing into a one-block gap
  below its plant, and support placed or removed under a head. The replay builds each case in a fresh world with the
  recording random as the level's, dispatches the state as `AbstractLevel.TickChunkBlocks` does (its
  `isRandomlyTicking`, then its generated owner), runs the actions, and compares the draws, the ordered writes and
  their neighbour, client and known-shape flags, the final blocks, that nothing else in the chunk around them changed,
  and the ticks pending in the chunk with their delays. Six cases list exactly where the block notification pipeline
  still parts from vanilla (see
  [reactions to block updates](known-gaps/vanilla-blocks-reactions-to-block-updates.md)).

- `melting_weather.json` (`VanillaMeltingWeather.ReplayMeltingAndWeatherReference`) records ice, frosted ice and
  snow layers melting and the chunk tick's precipitation pass, from `Java/MeltingWeatherFixtures.java`. The probe runs
  a dedicated server of its own with the game frozen. Cases use a normal overworld (sea level 63), or a flat
  overworld (`generator: flat`, sea level -63); the driver groups them into separate server runs. Each case fills the nine chunks around chunk (0, 0) with its biome and blocks, waits for vanilla's
  light engine and records the light the behaviour reads, which the replay writes into its chunks as input rather
  than relighting. `random_tick`, `tick` and `place` cases run one block's random tick, scheduled tick or placement
  with the level random seeded; `precipitation` cases run `ServerLevel.tickChunk` on chunk (0, 0) a number of times
  with the level random and `randValue` set, over layers tiled across the nine chunks. Each records the blocks that
  changed, the block and fluid ticks scheduled with their delays, the next random draw and, for precipitation, the
  position sequence afterwards. The replay drives Obsidian's random-tick binding, `RunScheduledTick`, a block write
  and `TickRandomBlocks` on the case's level with a player in chunk (0, 0), the only chunk whose neighbours are all
  loaded. Sparse frost cases capture immediate neighbour collapse, including removing an end of a trail. A
  `biomeBorder` selects the other biome at or above a quart coordinate on x, y or z; `biomeSamples` records native
  full-resolution lookups with `biomeSeed`, including all four horizontal chunk borders. Entity cases install
  armor stands without ticking them and record `entityPositions` after snow grows; standing and edge-overlapping
  entities rise, while those above or outside the new collision volume stay put. The replay also checks client
  position correction in one focused Obsidian packet test.

- `furnace_results.json` (`VanillaFurnaceResults.ReplayFurnaceResultReference`) records what vanilla's furnaces,
  blast furnaces and smokers give out, from `Java/FurnaceFixtures.java` (`CaptureFixtures=furnace_results`). Each case
  is its input: the machine, the recipes it cooked and how often, the recipes the player already knows, the
  `limited_crafting` rule, a seed for the level's random and whether a player takes the result
  (`awardUsedRecipesAndPopExperience`) or the machine is broken (`preRemoveSideEffects`). The probe decodes the
  recipes from the vanilla data pack and gives the player vanilla's recipe book; it records the recipes awarded, those
  the book added, the criterion triggers fired, the recipes `recipe_crafted` fired for, the orbs, the draws and how
  many recipes the machine still holds. A machine walks its recipes in a hash order, so lists are sorted and cases
  with several recipes give at most one a fractional experience. The replay clicks each take case's result as a
  client does, then lets the player leave, which gives them what the take queued, and removes each break case's
  machine, with the case's seed as the level's cooking random. It compares the recipes recorded as known, the
  `recipe_unlocked` and `recipe_crafted` triggers (through advancements it adds for each recipe), the orbs spawned,
  what the machine still holds, the bonus points' draws and the level random's draws. Where Obsidian knowingly
  draws differently (see [workstation details](known-gaps/workstation-details.md)), the replay asserts the
  difference by name, pinned to what Obsidian draws, so that it fails once the difference goes.

- `block_contact_scenarios.json` (`VanillaBlockContact.ScenariosMatchVanilla`) records entities touching pressure
  plates and tripwires, from `ScenarioContactCaptures.cs` over RCON like the redstone scenarios
  (`CaptureFixtures=block_contact`). It is its own fixture, not more redstone scenarios, because its entities are
  inputs the redstone fixture can't state (a type, NoAI and NoGravity, an item's stack, a teleport each tick, removal
  and death) and its checkpoints record where each entity is as well as the blocks. A scenario lists its box, its setup
  and its entity events: `summon` (with `type`, `pos`, and `item`, `noAi`, `noGravity` or `motion`), `move` (a
  teleport), `remove` (moved below the world and killed, so it leaves at once) and `kill` (a mob dies as usual,
  keeping its place for its death animation). The cases cover every kind of plate (wooden, stone, light and heavy
  weighted) with items, armor stands, pigs and husks put on, lowered onto, slid onto and walked across them, two
  entities arriving on consecutive ticks and leaving apart, a dying pig, one entity on two plates, entities that never
  touch blocks (a marker, a display, an interaction and an area effect cloud, which a plate still counts), and
  tripwires with and without hooks, entered from the side and from above, with an entity present at the recheck.
  Entities that move do so by teleports with gravity off, so the replay tests contact and not movement physics, which
  differs today: Obsidian's items fall a tick late and slide too far, its armor stands don't move, and its NoAI mobs
  fall, where vanilla's float.
  The replay runs every scenario in one test server's world, each in its own box with a player high above it, spawns
  the same entities, runs each event before its tick and compares the blocks and the entities' positions every tick.

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

dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=fire

dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=ticking_blocks

dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=block_use

dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=eyeblossom_ghast_portal

dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=turtle_eggs

dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=frogspawn

dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=growing_plant_ticks

dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=melting_weather

dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=furnace_results

dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=block_contact

dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=sulfur -p:CaptureCases=spike_shatters_after_falling+review_spike_pending_fall_keeps_frustum
```

This uses the server of the Minecraft version `Obsidian.csproj` targets, from the shared vanilla cache (see
[vanilla captures](vanilla-captures.md#the-vanilla-cache)), and needs Java 25. The sulfur capture takes about 30 seconds.
To add a scenario, add its definition with placeholder checkpoints (the redstone capture records as many ticks as the
scenario has checkpoints) and capture again; the tests pick it up.

For redstone, `CaptureCases` accepts exact `+`-separated scenario names and records only those; the other
scenarios keep what they hold.

`block_contact` is captured only when named, and takes about 15 seconds for every scenario. A scenario gives its
number of `ticks` and starts with empty `checkpoints`; `CaptureCases` records only the scenarios it names, the others
keep what they hold.

For sulfur, `CaptureCases` accepts exact scenario names separated by `+`. It replaces only those cases' checkpoints
and skips the Java probes. Other scenarios retain their stored absolute times and are replayed independently.
`SulfurFixtures.java` also accepts an optional second argument of exact `+`-separated probe names, when run directly
with the vanilla server and libraries on the classpath, to re-record only new or changed Java cases.

For `fire`, `CaptureCases` accepts exact `+`-separated case names in `fire_ticks.json` and records only those
cases' runs; the others keep theirs.

For `ticking_blocks`, `CaptureCases` accepts exact `+`-separated names and records only those Java cases. For example,
`dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=ticking_blocks -p:CaptureCases=review_dripstone_mud_in_nether`.
Append new inputs, select only their names, and verify existing case text is unchanged. The Java probe accepts the
same selection as its second argument when run directly with the cached server and libraries on its classpath.
`scheduled` records scheduling attempts: the first request per position/type remains pending, even when later
attempts use another delay. The pending-coral case records delays 85 and 88, but only the first is pending.

For `eyeblossom_ghast_portal`, `CaptureCases` accepts exact `+`-separated names in
`eyeblossom_ghast_portal_ticks.json` and records only those cases; the others keep theirs. The capture takes about ten
seconds.

For `more_blocks`, `CaptureCases` accepts exact `+`-separated names in `more_blocks.json` and re-records only those
Java probes. No RCON server is started and no other fixture is changed. Its source and the reusable runtime
instrument dumper are separate so editing these probes does not regenerate unrelated assets.

The `placement` capture takes about 10 seconds after the server starts and re-records every item; to add a context,
add it to `contexts` and capture again. For `block_use`, add inputs to `cases` and use `CaptureCases` with exact
`+`-separated names to record only the added cases. A setup's `text` seeds both sign faces, and `handUsed: offhand`
selects the other hand. An optional `randomSeed` seeds the level random immediately before use; the failed-growth
case verifies that a valid bone-meal target consumes the item even when its growth chance fails. Optional checks
record sign text flags and editor, attack charge after a started and a refused swing, explicit and periodic
container close sources, and awarded advancement criteria. The fixture lists
location predicate terms in Obsidian's existing format; the probe converts them to 26.3's `all_of` and `match_block`
encoding before registering them with vanilla's advancement system.

The `turtle_eggs` and `frogspawn` captures run no server and re-record every case. To add one, add a case with its
`name`, `seed`, `origin` and `setup` (and `time`, `dimension`, `place` or `tick`) and capture again; pick a seed whose
first draw takes the branch the case is about. The `furnace_results` capture also runs no server and re-records every
case.

The `growing_plant_ticks` capture runs no server and re-records every case. To add one, add a case with its `name`,
`seed`, `setup` and an `origin` or `actions`, and capture again; pick a seed whose first draws take the branch the case
is about.

For `melting_weather`, `CaptureCases` accepts exact `+`-separated case names and records only those, leaving the
other cases unchanged. For example, add a case's inputs to `cases` and capture it with
`-p:CaptureFixtures=melting_weather -p:CaptureCases=review_frosted_pair_collapses`. Each generator group takes
about 10-20 seconds after the driver builds; it does not run other fixture captures.

A `structure_pieces` case with `captureReferences: true` also records `referenceChunks`: the saved
`structures.References` from vanilla's chunk serializer at STRUCTURE_REFERENCES, across its bounding box plus
one chunk of border. `VanillaStructureSaves.GeneratedChunksSaveVanillaReferences` compares generated chunk saves
with those compounds, including empty border chunks and packed start positions at negative coordinates.

For `structure_pieces`, `CaptureCases` accepts exact `+`-separated case names and re-records only those starts. The
capture takes about 15 seconds after the server starts. `StructurePieceFixtures.java` is an asset input like the
other dumpers, so editing it regenerates the assets once.

The underlying command is `Obsidian.AssetGenerator capture-scenarios <minecraft version> <cache directory>
<scratch directory> <test assets directory>
[redstone+fluids+daylight+sulfur+more_blocks+structure_pieces+heightmaps+saved_heightmaps+mining+placement+
copper_weathering+fire+ticking_blocks+block_use+turtle_eggs+frogspawn+growing_plant_ticks+melting_weather+
furnace_results]
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
