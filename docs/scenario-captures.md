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
  drops. `hopper_fills_chiseled_bookshelf`, `hopper_drains_chiseled_bookshelf`,
  `dropper_fills_chiseled_bookshelf_from_behind`, `hopper_fills_decorated_pot` and `hopper_drains_decorated_pot`
  move items into and out of the blocks that hold them, each read by a comparator into a dust: the shelf's slots
  and the last slot used, and how full the pot is. A scenario naming an `item` also records in each checkpoint the total count of that item lying as item
  entities within two blocks of its box, and the replay counts Obsidian's the same way, so it must be `level`.
  Consumers and sources with no other capture each have one: `doors_open_from_power` (an oak and an iron door,
  powered beside either half), `tnt_primed_by_power` (beside a new redstone block, and placed beside one),
  `rails_powered_along_a_line` (ten powered and ten activator rails, the last of each out of reach),
  `crafter_crafts_into_chest`, `dropper_pushes_into_chest` and `hopper_pushes_into_chest` (each read through a
  comparator into a dust), `copper_bulb_toggles_comparator`, `bell_powered_by_redstone`,
  `sculk_sensor_reads_note_frequency` (a note block's vibration, whose frequency a comparator reads while the sensor
  is active) and `calibrated_sculk_sensor_filters_frequency` (only the sensor whose back signal matches the note's
  frequency activates). The capture kills TNT a scenario primed when it ends, so it can't explode during a later one.
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

- `lightning_bolts.json` (`VanillaLightning.ReplayLightningReference`) records lightning bolts from
  `Java/LightningFixtures.java` (`CaptureFixtures=lightning`), on a dedicated server of its own in a flat world of air
  (plains, seed `test-seed`, the replay world's), frozen, whose level ticks the probe runs itself between the server's
  own. Each case runs in a chunk of its own, forced, at origin (32i + 8, 64, 8). RCON can't seed a bolt's random or
  hear sounds, level events and game events, so this is a Java probe rather than an RCON scenario. A `strike` case
  sets its blocks without updates and adds its mobs without AI, seeds the level's random, adds a bolt whose own random
  is seeded (its seed and flashes drawn again, as its constructor draws them), optionally visual only or with the
  listener as its cause, and records after each tick the blocks the level stored in the area (in order, with every
  flag, through the block use probe's observed jar), the blocks that changed, the entities (type, position, remaining
  fire ticks, health, persistence and what lightning changes), the sounds and level events a spectator received and
  the game events within 16 blocks, then the lightning advancements the spectator was granted. The spectator stands
  where the bolt can't strike it, and spectators don't count for natural spawning, so no chunk is shuffled with the
  level's random between the strike's draws. Mobs' own randoms aren't seeded, so their ambient sounds, a witch's
  drinking and whether a pig's zombified piglin holds a golden sword or spear aren't recorded. A `natural` case runs
  `ServerLevel.tickThunder` once on its chunk in a thunderstorm, with the level's random seeded and `randValue` set,
  and records the entities it added, `randValue` afterwards and the level random's next `nextInt()`.
  `inhabitedChunks` sets neighbouring chunks' relative `offset` and inhabited `time`; their entities are included
  when recording a redirected strike. The copper golem case's `recordWeathering` also records the saved deadline
  relative to game time and the level random's next integer after each tick, including the strike's -1 deadline
  and the following tick's reschedule. The cases cover
  rods (up, sideways, weathered, waxed) powering lamps on their side and beside their support, copper cleared around a
  rod and under a bolt, fire on grass and planks with the fire spread rule on and off (26.3 has no `do_fire_tick`;
  `fire_spread_radius_around_player` is 0 or -1), easy (fire only on flashes) and peaceful (no conversions), a charged
  creeper, a zombified piglin, a witch, mooshrooms changing colour, a turtle (with mob drops off), items, a copper
  golem, a Breeze, an armor stand, a channeled bolt and the advancements, a visual-only bolt, and storms striking open ground, a
  rod (and not a covered one), a mob under the sky, a skeleton trap (none above a rod or without mob spawning), a
  redirected trap in a neighbouring chunk with a different inhabited time, a missed chance, no thunder and an empty
  column. The replay runs every case in one world, through the level's ticks
  with a ticking chunk ticket for each, and compares all of it; its pinned differences are listed in [the known
  gaps](known-gaps/lightning.md).

- `block_use.json` (`VanillaBlockUse.ReplayBlockUseReference`) records what using a block does, from
  `Java/BlockUseFixtures.java`. Like the placement probe it runs a dedicated server of its own in a flat world of air,
  and each case uses its main hand's item through `ServerPlayerGameMode.useItemOn`; optional `rayUse` also runs
  `ServerPlayerGameMode.useItem` when use-on passes. The area's chunks are forced, so
  block events run as a tick runs them. A case names its blocks, the clicked face, the item and count, the off hand,
  the game mode and sneaking. It records the result (which decides the swing), the blocks that changed, the hand,
  the entities that appeared, the sounds, level events and block events a second player beside the target received,
  the game events within 16 blocks (with their source), the statistics awarded and the menu opened, and when a menu
  opened, what closing it changed and played. The replay drives `UseItemOnPacket` in one world, with full sky light
  as the capture's open sky has, runs the queued entity actions and the tick's block events, and checks swings from
  the result. The original 92 cases retain their captured bytes. Another 285 cases cover buckets (72), chest
  obstructions (73), lighting (63) and the blocks that hold items (77: flower pots, chiseled bookshelves, decorated
  pots, including ones still waiting for their loot table, lodestone compasses and jukeboxes), including ordered block writes with every flag bit and successful block
  destructions. Fourteen redstone cases do too: opening a single and a double trapped chest powers the dust beside
  it and, through the block below, the dust beside that; clicking a comparator into subtract mode reads a chest
  under a sitting and a standing cat, a command block's `SuccessCount`, an active, a cooling and a calibrated sculk
  sensor's last frequency, a lit copper bulb and a copper golem statue's pose; and a lever rings a bell, flips a
  copper bulb, and extends and retracts a piston, with their sounds and game events. Their ordered writes now
  compare every flag bit directly with vanilla. These cases observe all writes, including positions absent from
  their setup; an unexpected position fails the replay. The older 92 cases compare final block states only. Extending ordered-write checks to those
  cases remains an open follow-up. The replay uses the capture's absolute coordinates: translating plant
  outlines changes their position-dependent offsets and can change a bucket ray hit. It also checks whether
  closing effects exist, rather than comparing them only when both sides happened to open a menu.
  `SpawnEggAmbientSoundMissing` explicitly asserts the sheep spawn egg's missing ambient sound; it fails if the
  sound starts working (see [mob ambient sounds](known-gaps/mob-ambient-sounds.md)). The expected differences of
  the newer cases are named, each pinned to its exact cases, and fail when they disappear (see below).

- `brushing.json` (`VanillaBrushing.ReplayBrushingReference`) records a player brushing, from
  `Java/BrushingFixtures.java` (`CaptureFixtures=brushing`). The probe runs a dedicated server of its own in a flat
  world of air, in a temporary folder it deletes. A case sets up suspicious sand or gravel (or another block) with its
  `lootTable` and `seed` or a `storedItem`, stands the user at `player` (head and last rotation the same) holding a
  brush (`damage`, `handUsed`, `gameMode`), clicks a `face` through `ServerPlayerGameMode.useItemOn` at tick 0, then
  steps `ticks` ticks as a server tick reaches the area: the game time, the due block ticks, the area's entities, then
  the user's held use (`LivingEntity.updatingUsingItem`). `actions` between ticks look or move elsewhere, release the
  use, set a block, choose another hotbar slot or click again. The probe wraps the level's neighbour updater to record
  each write with its flags (`UPDATE_NEIGHBORS` from its neighbour update, the rest from its shape updates; a change
  neither saw has null flags), tracks the user's entity so a bystander three blocks above sees it, and keeps each tick
  that did something: the click's result, the writes, the bystander's sounds (with source, volume and pitch), level
  events and particles, the user's entity events, game events, statistics, advancement criteria
  (`player_generates_container_loot`), the used hand, whether the use goes on, the block entity's saved data when it
  changed, its client update tag when its state was written, and the entities that appeared (items with position and
  motion, falling blocks with their state) or went. The 16 cases cover a full brush of sand from the top and of gravel
  from the north, a stored item, nothing inside, the off hand, releasing then decaying, looking away, moving out of
  reach, the block replaced, a block that isn't brushable, the brush breaking, creative, adventure, a second click
  inside the cooldown, another hotbar slot, and a block that falls after a stroke and breaks where it lands. The
  replay drives the use-item-on packet and the same tick order in a world of nine chunks, and compares every field of
  every kept tick. Two `hit_ground` events Obsidian doesn't send are pinned by case and tick (see [brushing and armor
  stands](known-gaps/brushing-and-armor-stands.md)). Item entities' yaw and sound seeds aren't recorded; dust
  particles are spawned by clients, so the server sends none, which the replay checks.

- `armor_stand_use.json` (`VanillaArmorStandUse.ReplayArmorStandUseReference`) records a player using an armor stand,
  from `Java/ArmorStandFixtures.java` (`CaptureFixtures=armor_stand_use`), on a dedicated server of its own in a
  temporary folder. A case gives the stand's flags (`showArms`, `small`, `marker`, `noBasePlate`, `disabledSlots`) and
  what it is `wearing`, the point `clicked` relative to the stand, and the user's `item`, `count`, `itemName`,
  `handUsed` and `gameMode`; the probe runs what `handleInteract` does once the target is in reach
  (`Player.interactOn`, then the trigger and the swing for a success) and ticks the stand once, which sends its
  equipment. It records the result, the hand, the stand's equipment and saved fields, the bystander's sounds, swings
  and equipment packets, the user's swings, game events, statistics and `completedCriteria`. The 41 cases cover each
  slot picked by height on a normal and a small stand, items going to their own slot, stacks giving one, swaps,
  disabled, removal-locked and placement-locked slots, hands without arms, markers, spectators, creative, adventure,
  the off hand, a named item and a name tag. The replay sends the interact packet and compares everything but the
  result itself, which decides the swing and trigger it compares.

- `waxing_bells_redstone_ore.json` (`VanillaWaxingBellsOre.ReplayWaxingBellsAndRedstoneOreReference`) records
  honeycomb waxing, bell ringing and redstone ore lighting by a use, from `Java/WaxingBellsOreFixtures.java`
  (`CaptureFixtures=waxing_bells_ore`). Its cases take `block_use.json`'s inputs, and the probe runs on
  `BlockUseFixtures.java`'s server, players, recorders and write observer, which it calls by reflection, so that probe
  is unchanged; the source launcher compiles it from the same directory. Every case records what a `block_use` case
  records with `checkWrites` and `checkEffects`, plus `userEffects`: the sounds, level events and block events sent to
  the acting player's own client, in order. Vanilla leaves the user out of the waxing sound and particles its client
  predicts, and sends it the bell's sound and block event. The 114 cases wax each of the 60 waxable blocks with
  non-default properties, both halves of a door and of a double copper chest (the other half takes the wax through its
  shape update), sneaking past and opening a door and trapdoor, creative, adventure, the last honeycomb, the off hand,
  blocks that can't be waxed, and `item_used_on_block`, which vanilla fires before the write and again after it (a
  location check sees the unwaxed block the first time and the waxed one the second). Bells
  are rung on each face of each attachment, above and below the rim, powered, with an item (which isn't placed),
  sneaking, in adventure and spectator, and from the off hand. Redstone ore is used lit and unlit, with nothing, a
  stick, or a block item that can and can't be placed beside it, sneaking, in each game mode and from the off hand.
  The replay drives `UseItemOnPacket` in one world and compares every recorded field from the start, ordered writes
  with every flag bit and swings included; two cases pin the door and trapdoor opening flags (see
  [waxing, bells and redstone ore](known-gaps/waxing-bells-and-redstone-ore.md)).

- `food_and_harvest.json` (`VanillaFoodHarvest.ReplayFoodAndHarvestReference`) records phase 2 batch A's
  123 cake, candle cake, berry, pumpkin, plant-head trim and hive uses, from `Java/FoodHarvestFixtures.java`
  (`CaptureFixtures=food_and_harvest`). It reuses `BlockUseFixtures`' real server, use dispatcher, packet and game
  event recorders and the observed jar's ordered writes. All cases compare every existing observable from the
  start, including every block-write flag bit and unexpected writes. `soundPackets` also checks exact transmitted
  positions, volume, pitch and category (the independent sound-variant seed is not a replay input).
  Additional outputs are `foodAfter`,
  `saturationAfter`, `inventory` (all 41 vanilla inventory slots), `smokey`, and `bees` (occupant count and each bee's
  target/anger state); every case also compares `dropPositions` exactly. Pumpkin cases compare `dropMotion` too. The spectator bystander hears effects
  but cannot be chosen as a bee's target. Players are indexed as entities so nearby-bee targeting runs normally;
  cleanup excludes them. Each case seeds the level random and the four harvest/carve loot streams with
  `randomSeed`, default 1234. It clears seven cells below the target for smoke range/obstruction cases. The fixture
  covers age and hunger boundaries, both berry yield outcomes, all six pumpkin faces, broken shears, offhand use,
  full inventories, creative bottle exchanges, trim criteria before and after the write, occupied and obstructed
  hives, both campfire types at the smoke boundary, bees at and beyond the harvest search box, and nested codec
  predicates with array or singleton-object terms, plus omitted tool/location predicates. `shippedAdvancement`
  reads criteria directly from vanilla's advancement resource; replay verifies those conditions against Obsidian's shipped definition. All recorded fields match without pins
  (see [food and harvest](known-gaps/food-and-harvest.md)). Capture only this fixture with
  `dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=food_and_harvest`;
  `CaptureCases` selects exact `+`-separated names as for `block_use`.

- `cauldrons.json` (`VanillaCauldrons.ReplayCauldronReference`) records using cauldrons, from
  `Java/CauldronFixtures.java` (`CaptureFixtures=cauldrons`), which runs on `BlockUseFixtures.java`'s server, players,
  recorders and write observer by reflection, as the waxing probe does. Its cases take `block_use.json`'s inputs and
  three of their own: `components` and `offhandComponents`, SNBT component patches for each hand's stack (a water
  bottle's potion, dye, banner patterns, a shulker box's contents), and `carried`, further stacks put into inventory
  slots by vanilla's numbers. Every case records what a `block_use` case records with `checkWrites`, `checkEffects`
  and `checkInventory`, plus `userEffects`, both hands (`hand`, `otherHand`) and dropped items, each stack with its
  components. The 95 cases pour each bucket into each kind of cauldron, pour lava and powder snow under water,
  flowing water, a waterlogged slab and lava, fill buckets from full and part-full cauldrons, pour and fill water and
  glass bottles at each level, wash dyed leather and wolf armor, banners with one and two patterns and dyed shulker
  boxes with contents, and hold things the table doesn't know; with stacked hands, a full inventory, the off hand,
  creative mode with and without the result already carried, adventure, spectator, sneaking and criteria. A bucket
  that doesn't fill goes on to its own use (`rayUse`). Cauldrons keep vanilla's quirk of awarding
  `minecraft:used minecraft:air` for the last water bottle poured into a water cauldron. The replay drives
  `UseItemOnPacket` (then `UseItemPacket`, with the capture's exact rotation) in one world and compares every
  recorded field from the start; an empty bucket's own use after the cauldron passes aims with that rotation, so its
  ray passes between the cauldron's legs as vanilla's does. Two cases pin a custom effect bottle's encoding (see
  [cauldrons](known-gaps/cauldrons.md)). `CauldronExchanges` checks what vanilla can't: an
  interrupted or raced exchange neither loses nor duplicates an item or a level, and a busy inventory is waited for
  outside the level's locks.

- `dispenser.json` (`VanillaDispensers.ReplayDispenserReference`) records what a dispenser does with each item, from
  `Java/DispenserFixtures.java` (`CaptureFixtures=dispenser`). Like the block use probe it runs a dedicated server of
  its own in a flat world of air, but frozen and stepped one tick at a time, with random ticks and mob griefing off.
  A case is its input: the dispenser's facing and items, the blocks set up around it without updates, and the mobs
  standing in front (without AI; `tamed` and `canPickUpLoot` set). The probe powers the dispenser with a redstone block
  placed behind it, with every update, and after each of the case's ticks (6 by default; it fires on the fourth)
  records the blocks within 3 of the dispenser that changed since before the redstone block, the dispenser's items,
  the entities within 5 (dropped items summed by item, since item entities merge, or only their ids, or only that
  some dropped, where a case's `drops` says vanilla's are random; primed TNT with its fuse; mobs with their equipment
  and whether they can be sheared), and the sounds (positioned and following an entity, without mobs' ambient
  sounds), level events and particles a player beside the dispenser received and the game events within 16 blocks
  during that tick. The replay builds each case in one world of empty chunks, places the same redstone block and runs
  whole ticks of the level, comparing every field of every tick; game events are seen as they're emitted
  (`AbstractLevel.GameEventQueued`), since the level dispatches them later in the tick. Its mobs are spawned standing
  on their floor. Projectile positions aren't recorded: vanilla draws their spread from each projectile's own random.
  Three cases pin exact output values for their documented gaps, while every other field still compares with vanilla.
  Opt-in `details: true` cases compare full component payloads (an input item's optional `nbt` is its saved stack),
  shulker contents, equipment, rocket state, deterministic sound details/positions, particle positions and velocities.
  `flightMotion` records projectile velocity: the initial comparison permits spread, then subsequent ticks compare
  the captured drag and vertical acceleration against Obsidian's own previous velocity. Only exact random flight
  coordinates, sound seeds, random arrow-hit pitch and TNT's random horizontal impulse are omitted (see
  [dispenser behaviours](known-gaps/dispenser-behaviours.md)).

- `projectile_block_hits.json` (`VanillaProjectileHits.ReplayProjectileHitReference`) records projectiles hitting
  blocks, from `Java/ProjectileHitFixtures.java` (`CaptureFixtures=projectile_hits`). Like the block use probe it
  runs a dedicated server of its own in a flat world of air, at easy, with mob griefing off and time, weather and
  spawning stopped. Each case is its input: the blocks set up around the target, the projectile, where it starts
  relative to the target and its motion, whether it burns (`burning`), a trident's `enchantments`, an owner of
  `none`, the egg's `seed`, the potion, the item the owner holds, `thundering`, game `rules` and the `criteria` to
  register for the owner. The probe spawns the projectile, owned by a player, and calls `ServerLevel.tick` the case's
  `ticks` times from one task, so nothing else ticks. After each tick it records under `record` the blocks of the
  area that changed, the signal the cell east of the target receives, the entities of the area (the case's projectile
  as `projectile`, experience orbs by type alone), the sounds, level events and block events a bystander receives,
  the game events, the ordered writes with every flag bit and the blocks destroyed (through `BlockUseInstrumentation`'s
  observed scratch jar), and the criteria and statistics the owner gained; a field with nothing to report is left
  out. The owner and the bystander stand nine blocks away, where experience orbs and dragon's breath don't reach them:
  vanilla's are players the level knows of but not entities of its sections, while the replay's are entities. The
  replay ticks a test server's world the same way and compares every field of every tick, with each known difference
  pinned by case and field ([projectile block hits](known-gaps/projectile-block-hits.md)). A case with `boltSeed`
  and `levelSeed` seeds the lightning bolt its hit calls down, after the tick it appeared in and before its first
  (the bolt's own random, its seed and flashes drawn again, then the level's random its strike draws from): the
  Channeling case, whose bolt's life and copper sparks are otherwise random. The replay seeds them at the same point,
  runs that case with its players as spectators on a ticking chunk ticket (the capture's players aren't in vanilla's
  chunk map, so its storm draws no strike chances) and, like the capture, with random ticks off.
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

dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=waxing_bells_ore
dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=projectile_hits

dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=dispenser
dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=cauldrons

dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=eyeblossom_ghast_portal

dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=brushing

dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=armor_stand_use

dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=turtle_eggs

dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=frogspawn

dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=growing_plant_ticks

dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=melting_weather

dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=furnace_results

dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=block_contact

dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=lightning

dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=sulfur -p:CaptureCases=spike_shatters_after_falling+review_spike_pending_fall_keeps_frustum
```

This uses the server of the Minecraft version `Obsidian.csproj` targets, from the shared vanilla cache (see
[vanilla captures](vanilla-captures.md#the-vanilla-cache)), and needs Java 25. The sulfur capture takes about 30 seconds.
To add a scenario, add its definition with placeholder checkpoints (the redstone capture records as many ticks as the
scenario has checkpoints) and capture again; the tests pick it up.

For redstone, `CaptureCases` accepts exact `+`-separated scenario names and records only those; the other
scenarios keep what they hold.

`review_retraction_notifies_base_neighbours` covers the moving base's immediate neighbour notification: a
quasi-connected piston extends in tick 1. `review_compare_mode_same_output_notifies_front` in `block_use.json`
covers a compare-mode click with unchanged output waking the piston beyond its front support. The block-use
replay compares their writes with vanilla's exact flags; no `RedstoneWriteFlags` pins remain.

`calibrated_sensor_frequency_table` records `VibrationSystem.getGameEventFrequency` for the entire game-event
registry, plus native calibrated-sensor acceptance of landings, bounces, deaths and sensor clicks at matching
and mismatching filters. The replay checks every event Obsidian exposes and the captured acceptance cases.
The two `lever_*_piston` cases also opt into seeded pitch, sound packet seed and next level-random checks for
both extension and retraction. `CaptureCases` can refresh each of these cases independently.

`block_contact` is captured only when named. Its original 16 RCON scenarios take about 15 seconds each. A scenario
gives its number of `ticks` and starts with empty `checkpoints`; `CaptureCases` records only the exact names supplied,
and the other cases keep their recordings.

The 117 `contactMoves` scenarios use `Java/BlockContactFixtures.java` on a real dedicated server, with the same
scratch-only block-write instrumentation as block uses. They drive native `Entity.move` and
`applyEffectsFromBlocks` with controlled velocities: falls, stationary contacts, walking and external pushes.
This boundary exercises native collision, fall accounting, restitution and block hooks without AI or travel forces.
Each case has a seeded level random and each living entity an `entitySeed`; `noGravity` also controls native
restitution's gravity compensation. Inputs may set initial `fallDistance`, equipment, effects, age and health.
`jumpBoost` supplies an amplifier (zero for Jump Boost I), and a tick event's `removeJumpBoost` names the entity
whose effect is removed before movement. `recordSafeFallDistance: true` also records the living entity's effective
safe-fall-distance attribute at every checkpoint, including checkpoint zero. The review cases cover water at impact,
water crossed before a dry landing, fluid touching only the body's edge, slime and bed support outside the centre,
and Jump Boost I, II, a control fall and effect removal.

A summon's `feet` puts an item in a living entity's feet slot (leather boots for powder snow), and `sneaking` holds
its shift key down. A tick event's `jump` runs the named entity's `jumpFromGround` before movement. A summon of
`minecraft:player` makes a player without a client (its packets are kept, like the watcher's) that plates and
tripwires find, with `pose` naming its vanilla `Pose` (`STANDING`, `CROUCHING`, `SWIMMING`, `SLEEPING`); the replay
joins a test player in that pose. The probe never ticks its entities, so their `oldPosition` stays where they were
summoned; the replay sets the same start position. The `speed_*`, `jump_*`, `cobweb_*`, `berry_bush_*`,
`powder_snow_*`, `scaffolding_*`, `slime_step_*` and `pose_*` cases cover block speed and jump factors (from the block
an entity is in and the one below its support), being stuck in cobwebs, berry bushes and powder snow, berry bush
damage, powder snow and scaffolding collision for the entity moving, slime's step damping, and posed player boxes on
tripwires and plates. Their boxes are one row of three cells, the smallest that holds each setup.

`stationaryPlayer: true` skips movement entirely so that it cannot hide a stale stored bounding box. An
`updatePose` event names a player, sets its `sneaking` flag and asks vanilla to choose its pose; `tickAir: true`
also runs its base tick (the replay runs the player's ordinary tick). These cases record and compare pose,
both box corners, eye position, the swimming flag and air supply alongside the usual observations. The replay
checks both the stored and current box, and both eye-position readers. `pose_stationary_crouch_stand` checks
shrinking and growing without movement. `pose_stationary_auto_crawl_water` checks a non-swimming player forced
to crawl beneath a ceiling in water: air drops to 299 and then 298 at the crawling eye height.

From checkpoint zero onwards, the replay compares every recorded block, position, velocity, fall distance,
grounded flag, health and last accepted damage type; ordered block writes with their exact flags; unexpected
writes anywhere; drops; sounds in order with category, position, volume and pitch; level events, block events and
game events. It also compares the next level random integer after each case. Sound playback seeds come from an
independent generator and are not recorded. The initial 16 cases retain their original block/position checkpoints.
`VanillaBlockContact.ScenariosMatchVanilla` collects all failures across both kinds of case in one test.

For example, to record only a changed contact case:

```sh
dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=block_contact -p:CaptureCases=egg_step_stationary_later_tick
```

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

For `dispenser`, `CaptureCases` accepts exact `+`-separated names in `dispenser.json` and records only those cases;
the others keep theirs. Add a case with empty `checkpoints` and capture it on its own. The whole capture takes about
20 seconds after the server starts.

For `lightning`, `CaptureCases` accepts exact `+`-separated names in `lightning_bolts.json` and records only those
cases; the others keep theirs. Every case takes well under a second after the server starts. The probe needs the block
use probe's observed copy of the jar, which the capture builds first.

For `eyeblossom_ghast_portal`, `CaptureCases` accepts exact `+`-separated names in
`eyeblossom_ghast_portal_ticks.json` and records only those cases; the others keep theirs. The capture takes about ten
seconds.

For `brushing` and `armor_stand_use`, `CaptureCases` accepts exact `+`-separated case names and records only those;
the others keep theirs. Each capture takes about ten seconds after its server starts. Their probes write their worlds
to a temporary folder outside the checkout and delete it.

For `more_blocks`, `CaptureCases` accepts exact `+`-separated names in `more_blocks.json` and re-records only those
Java probes. No RCON server is started and no other fixture is changed. Its source and the reusable runtime
instrument dumper are separate so editing these probes does not regenerate unrelated assets.

The `placement` capture takes about 10 seconds after the server starts and re-records every item; to add a context,
add it to `contexts` and capture again. For `block_use`, add inputs to `cases` and use `CaptureCases` with exact
`+`-separated names to record only the added cases. A setup's `text` seeds both sign faces, and `handUsed: offhand`
selects the other hand. An optional `randomSeed` seeds the level random immediately before use; the failed-growth
case verifies that a valid bone-meal target consumes the item even when its growth chance fails. Optional checks
record sign text flags and editor, attack charge after a started and a refused swing, explicit and periodic
container close sources, and awarded advancement criteria.

Further optional inputs, for blocks that hold items, are recorded after everything else and change nothing the other
cases record. A setup placement's `nbt` loads that block entity data (SNBT without the id and position) as a chunk
load would, telling no comparator. `checkBlockEntity` records the clicked block entity's saved data, as a chunk saves
it without its position, as `blockEntity`. `comparator` names a comparator's offset from the setup: once the use is
recorded, its pending tick runs and its output is recorded as `comparatorSignal`, which shows both that it heard of
the change and what it read. `breakAfterUse` makes the user break the clicked block afterwards with what they hold,
and records under `broken` what changed, the entities around (with their items' components), what the bystander
heard and the game events and statistics. The replay compares all of them: SNBT is compared as Obsidian writes the
same NBT, the comparator's tick runs by ticking the level's scheduled ticks twice, and a break goes through a
finished dig. The fixture lists
location predicate terms in Obsidian's existing format; the probe converts them to 26.3's `all_of` and `match_block`
encoding before registering them with vanilla's advancement system.
Criteria without a location predicate, including `filled_bucket` with item predicates, are registered unchanged.
The pickup cases check both an unconditional criterion and the filled item's predicate: vanilla checks its
temporary result after inventory insertion, which can have emptied that stack even though the inventory received it.

New `block_use` probe inputs are optional, so existing cases record exactly their earlier fields:

- `player: [x, y, z, yaw, pitch]` overrides the actor's standing position and aim for that case. `rayUse: true`
  records `useOnResult`, then uses the item from its own POV when that result allows it. This follows the client's
  use-on/item-use dispatch for a block hit; a client looking only at a fluid source sends the item-use packet
  directly. Water and lava buckets ignore the earlier use-on destination. Powder snow buckets are block items.
- `dimension: "minecraft:the_nether"` uses the real Nether level and its environment attributes, with that area's
  chunks forced too. This captures water evaporation, its eight large-smoke particles, bucket cost and absence
  of a fluid-place game event.
- `cats: [{"offset": [0, 1, 0], "sitting": true}]` adds a cat before the snapshot. Sitting and standing cats are
  varied independently of conducting blocks and of either double-chest half.
- `checkInventory: true` records all 36 storage slots in vanilla slot order, each stack followed by the components
  it changes from its item's as SNBT, with a lodestone tracker's target relative to the target (compared as Obsidian
  writes the same NBT). `fullInventory: true` fills every storage slot except the hand with stone. Every case starts
  with nothing in its inventory but its hands. The replay compares storage, the hand and dropped entities together.
- `checkEffects: true` records server particle packet types and counts, and each sound's position and volume
  alongside the existing sounds, level events, game events and statistics. The replay compares every added field.
  Candle smoke comes from client-local particles; campfire shovel dousing sends level event 1009. Neither sends
  a server particle packet. Random sound pitches are not fixture fields.
- `checkWrites: true` records ordered `writes` (offset, state, all integer flag bits) and `destroyed` blocks
  (offset, previous state, drops requested), from use through the block-event drain. The capture adds observation
  callbacks to `LevelChunk` storage and successful `Level.destroyBlock` returns in an unsigned scratch copy of the
  vanilla jar; the cached jar stays untouched. The observer does not replace block behavior or filter positions.
  The replay uses `LevelFluids.BlockStored` and `AbstractLevel.BlockDestroyed` for the matching observations.

For example, after adding a case, run
`dotnet build Obsidian.AssetGenerator -t:CaptureVanillaScenarios -p:CaptureFixtures=block_use -p:CaptureCases=bucket_water_evaporates`.
Select only added or deliberately changed cases, preserve existing line endings, and verify the original case
text is unchanged. Drops are checked with deterministic sapling and torch cases; grass's random seed loot is
not used as a fixed drop reference.

For `waxing_bells_ore`, `CaptureCases` likewise takes exact `+`-separated names in
`waxing_bells_redstone_ore.json` and records only those cases; the others keep what they hold.

For `cauldrons`, `CaptureCases` likewise takes exact `+`-separated names in `cauldrons.json`. The capture takes about
15 seconds.

For `projectile_hits`, `CaptureCases` accepts exact `+`-separated names in `projectile_block_hits.json` and records
only those cases; the others keep theirs. The capture takes about 15 seconds after the server starts.

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
copper_weathering+fire+ticking_blocks+block_use+waxing_bells_ore+cauldrons+turtle_eggs+frogspawn+growing_plant_ticks+
melting_weather+furnace_results+brushing+armor_stand_use+projectile_hits+dispenser+lightning]
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
