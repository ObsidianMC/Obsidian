# Core circuits (R3–R6)

Load [`00-index-and-rules.md`](00-index-and-rules.md) first. Requires the foundations in [`01-foundation.md`](01-foundation.md).

## R3 — Common directional power engine

### Status (2026-10-09): complete; four R4 replays now run and pass, five wait for R6

**Built:**
- `WorldData/Redstone/RedstoneEngine.cs`: vanilla `SignalGetter` queries, `GetSignal`, `GetDirectSignal`, `GetDirectSignalTo`, `HasSignal`, `HasNeighborSignal`, `GetBestNeighborSignal`. Conductors take the max of their own signal and the direct signal reaching them. Unloaded positions read as no signal and never load.
- `Redstone/IRedstoneBehavior.cs`: `IRedstoneBehavior` (signal and direct signal per direction) and `RedstoneBehaviors`, registered by vanilla block class. Blocks without a registration give no signal.
- `Redstone/RedstoneWire.cs`: wire output (nothing downward, full power upward, horizontal only toward a connected side) and power recalculation. It runs as a neighbor reaction and as a placement reaction, writing only when the power changes.
- `Redstone/SculkSensorSignal.cs`: sculk sensors and calibrated sculk sensors. Their power is read from the state they already write through `SetBlockAsync`, so sculk output now comes from the engine.
- `Redstone/RedstoneRegistration.cs`: one `Install` call for the level and the tests.
- R2 pipeline change: `BlockNotificationPipeline` now queues a placement reaction for the changed block first, the way vanilla's `onPlace` runs. Without it a newly placed wire never computed its own power.

**Vanilla facts verified from bytecode** (`dxg` SignalGetter, `egb` RedStoneWireBlock, `fmg` wire evaluator, `egt`/`eaf` sculk sensors):
- `getSignal` for a conductor is `max(own, getDirectSignalTo(pos))`; `getDirectSignalTo` is the max over the six neighbors' direct signals, capped at 15.
- Direction passed to `getSignal` is from the receiver to the neighbor. Neighbors are visited in vanilla order: down, up, north, south, west, east.
- Wire `getSignal`: 0 when wires are suppressed or the direction is down; `power` when up; otherwise `power` when the side facing the receiver is connected.
- Wire power is `max(bestNeighbor, strongestWireReached - 1)`, with wires suppressed during the best-neighbor query.
- Sculk sensor: `power` in every direction, direct only upward; calibrated gives nothing toward its facing.

**Queue:** power recalculation runs on the R2 pipeline queue, which is FIFO, coalesced while pending, and budgeted at 1000 per drain. R3 adds no second queue, so the plan's "enqueue, deduplicate, stable order, budget" requirement is met by that pipeline.

**Tests:** `Obsidian.Tests/RedstoneEngine.cs` (20 tests) and `Obsidian.Tests/BlockNotifications.cs` (8 tests), both on `RedstoneTestWorld`, which holds real `Chunk`s.
- Six faces: a source reaches only the receiver on the face it emits toward (Theory, six cases).
- Conductor boundary: direct signal crosses stone and not air; a weak-only signal doesn't cross a conductor.
- Analog: power at 15, 9 and 1 decays one step per wire along a 10-wire line.
- Deterministic: forward, reverse and repeated placement give identical wire powers and shapes.
- Unloaded chunk edge: a source in an unloaded chunk reads as no signal and loads nothing; the loaded control is powered.
- Wire output: connected sides and the block below are powered, open sides and the block above are not.
- Sculk sensor output and calibrated facing; a wire beside a sculk sensor takes its power.
- Behavior registration rejects duplicates.

**Test sources:** the R3 tests register a test source on a block class to give the exact signal each test needs. Basic sources (levers, buttons, torches, the redstone block) are R4, so there is no real-source test yet.

**Replay adapter:** `VanillaRedstone.EngineScenarioWorld` implements `IRedstoneScenarioWorld` over `RedstoneTestWorld`, with a ring of chunks around the box so its chunks tick. `ScenarioMatchesVanilla` runs the four R4 scenarios (see R4 below). The five scenarios that need repeaters, comparators, observers or reload stay in `ScenarioMatchesVanillaAfterR6`, skipped. `Reload` throws until R6.

**R3 correction (2026-10-09):** placement now retains the previous state in the queued placement work and invokes placement behavior only for a block-type replacement, matching vanilla `onPlace` rather than treating every property mutation as a placement. A newly placed wire first calls the shared `ShapeUpdater.UpdateFromNeighbourShapes` over its committed neighbors, commits that derived dot/line/cross state, and only then calculates power. This fixes dust placed beside existing wire/source blocks retaining default all-`none` sides. `RedstoneEngine` also clamps every behavior output at the common API boundary to 0–15. Regressions use a real generated signal source (`minecraft:redstone_block`) to validate own-side derivation and output, assert both wires reconnect when the second is placed, and verify negative/over-15 behavior values clamp. The existing analog fixtures now give every wire valid support; the line-end assertion correctly checks a perpendicular unconnected side, because vanilla extends a one-axis line at its endpoint.

**Results:** solution build succeeded. Focused R1–R3 tests: 45 passed. Before this correction, `linux-smoke`: 401 passed, 1 failed (the pre-existing `WorldPersistence.EntitiesAndContainersSurviveRestart`). The server started on the same build and reached ready with no errors. I have not played it in-game.

**Gaps and checks still open:**
- Replay validation: `dust_attenuation`, `source_timing_lever`, `torch_inversion` and `torch_burnout` match vanilla (R4). The rest wait for R6.
- Wire power is the steady state of vanilla's rule, reached through notifications, and each power change now also notifies a second ring of neighbors. Vanilla's turbo evaluator uses a different propagation order, so transient states may still differ at the same tick. R5 owns this: replace the cascade with a network evaluator and bring the drain budget back down.
- A wire's output checks its stored connection state, not a recomputed one. It is current because shape updates run on every change.
- Unloaded neighbors count as air for wire climbing.
- Shape-driven removals still clear the block without dropping an item.

### Work

- Add a level-owned `WorldData/Redstone/RedstoneEngine.cs` (exact layout is flexible).
- Provide internal directional queries for signal output, direct signal, best neighbor signal, and powered-neighbor checks.
- Model 0–15 analog power, source/receiver face, weak/direct power, conductor transmission, and wire-specific output.
- Register per-block behavior by material/block class. Do not distribute signal switches across unrelated level code.
- Enqueue and deduplicate affected positions after a change; process in stable order with a budget; only write changed states.
- Never force chunks to load/generate. Existing `BlockPhysics` conductor/source/class metadata is a starting point; extend `VanillaDumper.java` only when a needed vanilla property cannot be derived reliably.

### Exact repository facts

- `BlockFace`/`Vector.AllDirections` are used by `AbstractLevel.BlockUpdateNeighborsAsync`; keep one explicit and documented direction order for engine work.
- Block state changes use `IBlock.WithProperty(name, value)`; compare with `IsSameState`, not reference equality.
- Wire state uses `power` and `north`/`east`/`south`/`west` properties. The current shape code recognizes `RedStoneWireBlock`, `Repeater`, and `Observer` in `ShapeUpdater.cs` around lines 147–245.
- `BlockPhysics.IsRedstoneConductor` comes from bit 9 of generated per-state data. Do not equate `IsSolid`, collision, or signal-source membership with conductor behavior.
- Power queries must use loaded chunks directly: `level.GetLoadedChunk(x >> 4, z >> 4)?.GetBlock(x, y, z)`. `GetBlockAsync` deliberately calls `GetChunkAsync` and can schedule generation.

### Tests

All six faces; conductor/non-conductor boundaries; analog levels; deterministic queue order; unloaded chunk edge.

## R4 — Basic sources, player use, and simple consumers

### Status (2026-10-09): in progress; not a handoff gate

**Implemented so far:**
- `BasicRedstoneComponents` registers redstone block, lever/button, redstone torch, redstone ore and existing sculk state output on the common directional engine; it also owns lamp, copper-bulb, door/trapdoor/fence-gate consumer reactions.
- Lever and button interaction is dispatched before generic item placement, while sneaking preserves placement behavior. Button release is scheduled through persisted `block_ticks`; generated stone-set buttons use 20 ticks and wood/nether-wood buttons use 30 ticks.
- Mounted component placement derives `face` and `facing` from the clicked face/yaw before the ordinary committed placement write.
- Consumer registration supports both exact block names and vanilla block classes, with exact registrations taking precedence. Class-level placement reactions ensure a lamp, bulb, or openable placed beside an already-live source evaluates itself. Door-half propagation is handled as a paired state write through the common notification pipeline.

**Validation so far:** focused `RedstoneEngineTests` cover real redstone-block lamp/gate transitions, copper-bulb rising edges, mounted-source direct/weak behavior, torch directionality, and placement derivation. The latest full Linux smoke run was 411 passed with only the unrelated pre-existing `WorldPersistence.EntitiesAndContainersSurviveRestart` failure.

**Oracle-verified direct directions (2026-10-09, `tools/redstone` vanilla oracle, 1.21.11):** a powered floor lever gives direct power to the block below it (`power=15` beside that support) and none to the block above; a standing redstone torch gives direct power to the block above it and none to its support. Direction is source-to-receiver. The earlier lever and torch directions were mirrored and are corrected here. Wall-mounted direct directions follow from the same rule (wall = `facing` opposite) but have not been probed. Weak-power exclusions for torches are not yet oracle-checked.

**Client sync (2026-10-09):** the placed block's update now broadcasts the committed state after notifications run, so a newly placed dust connects for clients immediately.

**Note block and pressure plates (2026-10-09):**
- Note block: `powered` follows the neighbor signal; a note plays on the rising edge only, through `AbstractLevel.PlayNoteBlock`. Tested with a recording callback.
- Pressure plates (`WorldData/Redstone/PressurePlates.cs`): each level tick, players and mobs (non-spectators) press the plates whose top layer they overlap. Pressing schedules a persisted release tick (20 ticks for stone and wood plates, 10 for weighted); when it fires, a plate still occupied re-arms, otherwise it releases. Weighted plates output their count scaled to 15 (light: max 15; heavy: max 150).
- Oracle-verified (1.21.11): a pressed stone plate powers the block below it directly and its sides weakly, and does not power the block above. Armor stands ignore pressure plates; a zombie presses them.
- Tests: plate press, rearm and release, and weighted scaling are unit-tested through the test world. Entity detection and the real release timing through `AbstractLevel` are not yet covered by an automated test.
- Gap: item entities, projectiles and other non-living entities don't press plates yet. Wooden plates in vanilla are also pressed by arrows.

**Wall mounts and doors (2026-10-09):**
- Oracle-verified: a wall lever powers the wall it is mounted on (direct, toward `facing` opposite). A wall torch gives no direct power to its wall. Both were corrected in code.
- Doors: placement now creates both halves (`WorldData/Placement/DoorPlacement.cs`). Placement is refused, and the item kept, when the clicked face is the bottom, the space above isn't air, or the block below isn't sturdy. Facing comes from the player's yaw (`PlacementFacing`). The hinge chooses the side with more solid neighbours, and ties go left; this is a simplification of vanilla and has not been oracle-checked.
- Item consumption moved to just before the final write, so refused placements keep the item.
- Tests: door plan (both halves, refusal when blocked) and a powered door opening both halves.

**Mounted power reaches the support's neighbours (2026-10-09):** a lever or button (including its delayed reset) and a pressure plate now notify the neighbours of the block they are mounted on or stand on, as vanilla's `updateNeighborsAt` does. Before, a door against a powered wall never saw the power, because only the lever's own neighbours were notified. The fix is `BasicRedstoneComponents.SetMountedStateAsync` and `IBlockChangeWorld.NotifyNeighborsAsync`.

**Direction convention bug fixed (2026-10-09):** `RedstoneEngine.GetDirectSignalTo` passed the receiver-to-source direction, but vanilla `getDirectSignalTo` passes source-to-receiver. As a result, a conductor never took direct power from a source on its far side, so a powered wall could not power a neighbouring block. An R3 test had encoded the old convention and now uses the vanilla one.

**Placement and Breeze (2026-10-09):**
- Trapdoors (`WorldData/Placement/TrapdoorPlacement.cs`), checked against the 1.21.11 bytecode (`TrapDoorBlock.getStateForPlacement`): a horizontal click faces the trapdoor toward the clicked face, with the top half when the click is above the block's middle; a top or bottom click faces it away from the player, with bottom for a top click and top for a bottom click.
- Fence gates (`WorldData/Placement/FenceGatePlacement.cs`), checked against `FenceGateBlock.getStateForPlacement`: facing is the player's horizontal direction; `open` and `powered` both take the neighbor signal; `in_wall` is set when a wall tag block is on either side perpendicular to the facing.
- Breeze wind charges toggle levers through the same mounted-state path as a player, so the lever's support notifies its neighbours.
- Verification method: `javap -p -c -cp Obsidian.AssetGenerator/obj/vanilla/1.21.11/versions/1.21.11/server-1.21.11.jar <obfuscated class>`, with names from `server.txt`. The top-level `server.jar` is only a bootstrap and has no game classes.

**Known out-of-scope issue (2026-10-09):** wind charges themselves are broken in game. Reported by the user; not part of the redstone milestones, and not investigated. The Breeze lever toggling it calls is unaffected.

**Daylight detector (2026-10-09):**
- Output: `power` 0–15, weak only, in every direction (vanilla `getSignal`; there is no `getDirectSignal`). Registered through `BasicRedstoneComponents.Register`.
- Cycle: `WorldData/Redstone/DaylightDetector.cs`. `DaylightCycle` reproduces the overworld timeline: sky darkness is `(int)(15 - 15 × sky_light_level)` (vanilla `Level.updateSkyBrightness`). Sun angle is 360° swept over one period, eased by the cubic bezier `(0.362, 0.241, 0.638, 0.759)`. The keyframes are copied from `data/minecraft/timeline/day.json`. They are hand-written constants, not generated, and should move into the asset generator in a later pass.
- Output formula (`DaylightDetectorBlock.updateSignalStrength`, checked against bytecode): `raw = skyLight − darkness`; inverted gives `15 − raw`; otherwise, when `raw > 0`, the angle is smoothed toward 0 or 2π by 20% and `round(raw × cos(angle))` is taken; the result is clamped to 0–15.
- Update: a persisted block tick (`minecraft:daylight_detector`) that re-arms itself on the next multiple of 20 game ticks. Placement starts the first tick the same way (vanilla updates from a block-entity ticker on `gameTime % 20 == 0`).
- Right-click (not sneaking) inverts it and recomputes its power at once. Neighbors are notified only if the power changed, matching vanilla's flag 2 for the inversion and flag 3 for the power.
- Sky light comes from loaded chunks only (0 when unloaded), so a power query never loads a chunk.
- Verification: 33 vanilla oracle readings (`time set`, 40 stepped ticks, open sky, normal and inverted) in `Obsidian.Tests/DaylightDetector.cs`, all matched. The sample-to-game-time offset is not pinned: offsets 34–39 all reproduce every reading. The replay scenario will fix the exact phase.
- Gaps: overworld timeline only (nether and end are not modelled); sky light from the existing light engine has not been checked against vanilla at block edges; the detector's chunk must be loaded for it to tick.

**R4 completion pass (2026-10-09): partial. R4 is NOT complete.**

Done in this pass:
- **Replay enabled:** `ScenarioMatchesVanilla` runs `source_timing_lever`, `dust_attenuation`, `torch_inversion` and `torch_burnout` against the live engine, and all four match vanilla at every checkpoint. The other five scenarios (repeater, comparator, observer, save/reload) stay in `ScenarioMatchesVanillaAfterR6`, skipped until R6.
- **Replay world:** `RedstoneTestWorld` now owns a real `LevelBlockTicks`, and `Advance()` runs one game tick the way the level does (block ticks, then deferred simulation, then notifications). Its loads take a ring of chunks, because a chunk ticks only when its neighbours are loaded.
- **Shared redstone level interface:** `IRedstoneLevel` (`Redstone/IRedstoneLevel.cs`) is the one capability set the redstone behaviours need: committed writes (with or without neighbour notification), game and day clocks, sky light, persisted block ticks, and deferred simulation work. `AbstractLevel` and the test world implement it. Registration injects it, so behaviours don't cast.
- **Torch inversion and burnout** (`Redstone/RedstoneTorch.cs`), checked against `RedstoneTorchBlock` bytecode: a neighbour change that leaves a torch inconsistent with its support schedules a check two ticks later. A tick turns the torch off while its support is powered. The eighth turn-off at one position within 60 game ticks burns it out for 160 ticks. The support face is `Down` for a standing torch and `facing.Opposite()` for a wall torch.
- **Lamp** (`Redstone/RedstoneLamp.cs`), checked against `RedstoneLampBlock` bytecode: it lights at once on power, and goes out four ticks after power is lost if still unpowered. Both changes write without notifying neighbours. The turn-off delay was oracle-verified (lit until the fourth tick, off at the fourth).
- **Redstone block:** gives weak power only. Oracle-verified: a stone beside a redstone block does not carry its power up to a lamp above.
- **Placement state versus `setblock`:** vanilla derives a player-placed lamp's `lit`, and a door's `open` and `powered`, in `getStateForPlacement`. `/setblock` doesn't run that, so it keeps the state it was given. Obsidian's placed-notification reactions for lamps and openables were not vanilla and have been removed. Player placement now derives the state in `UseItemOnPacket`. The copper bulb keeps its placed reaction, because vanilla runs the same power check in its `onPlace`.
- **Waterlogging of placed blocks** (`Placement/Waterlogging.cs`): a waterloggable block placed where there is water takes `waterlogged=true`. Vanilla's flowing-water rule is assumed, not oracle-checked, because the placement path can't be driven by the oracle.

**Torch through a wire, and direction convention (2026-10-09):**
- Reported in game: a torch on a support fed by a wire didn't invert, while a lever on the support did.
- Two real causes. (1) Direct power was asked for with the wrong direction. Vanilla `getDirectSignalTo` (bytecode of `SignalGetter.d_`) passes the direction from the **receiver to the neighbour**, which is the face itself, not its opposite. The engine now does the same, and every direct rule was flipped to match: mounted sources ask with `AttachedDirection.Opposite()`, a standing torch and a sculk sensor answer `Down`, a pressure plate answers `Up`. Weak `getSignal` rules already used this convention. Oracle check: a wire under a support does not power it, and the torch stays lit (`WireUnderTheSupportDoesNotTurnTheTorchOff`).
  (2) A wire's power change didn't re-check blocks two steps away. Vanilla `updateNeighborsOfNeighbors` notifies the wire's neighbours and then the neighbours of each of them, so a torch on a support beside the wire is re-checked. `RedstoneWire` now does the same when its power changes. Oracle: removing and restoring the wire's source turns the torch on and then off again (`TorchOnASupportFedByAWireGoesOffAndRelightsWhenTheWireLosesPower`).
- A torch placed onto an already powered support is checked on placement (oracle: a torch placed onto a powered stone turns off).
- Cost: the second ring multiplies notifications, so `BlockNotificationPipeline.MaxWorkPerDrain` rose from 1000 to 10000. At 1000 the dust replay dropped up to 336 items and went stale. Still bounded, and drops are counted. R5 should replace this cascade with a network evaluator (vanilla's turbo wire evaluator) and then tighten the budget again.
- `IRedstoneBehavior.GetDirectSignal` documents the receiver-to-source direction.

**Redstone torch, lamp and wire timing (2026-10-09, status of the R4 pass):**
- Torch inversion, two-tick checks, the eighth-toggle burnout and the 160-tick recovery are implemented in `Redstone/RedstoneTorch.cs`. Oracle checks cover the wire-fed support cases: a wire beside a support powers it; a wire under a support does not.
- The lamp (`Redstone/RedstoneLamp.cs`) lights at once on power and goes out four ticks after power is lost. Oracle-verified.
- Placement state: a player-placed lamp is lit by a signal, and doors are opened and powered together, both derived in `UseItemOnPacket` (vanilla `getStateForPlacement`). `/setblock` keeps the state it was given.
- Replays: `ScenarioMatchesVanilla` covers the four R4 scenarios and all four match.

**Still required before R4 is complete:**
- **Tripwire and hooks:** implemented (see the tripwire pass below). Shears disarming and the timing of entity contact are open.
- **Target block:** implemented (see the target pass below). Only arrows reach it so far; other projectiles still need the 8-tick hit.
- **Player use coverage on a real `AbstractLevel`:** the use paths are unit-tested against the test world, not through `UseItemOnPacket` with a live level.
- **Lever and button client sync:** not written. Sneak use is covered (see the 2026-10-09 R4 pass below).
- **Waterlogging:** the placement rule is implemented; it is not oracle-checked because placement can't be driven through the oracle.

R4 is therefore not complete. R5–R10 remain unstarted.

### Work

- Add generic block-use dispatch before placement in `Net/Packets/Play/Serverbound/UseItemOnPacket.cs`; retain current containers and note/jukebox behavior.
- Add **component placement-state derivation** before generic final placement. The current packet mostly places `BlocksRegistry.Get(itemType)` defaults and only special-cases end-portal-frame facing. Derive valid component state from clicked face, cursor, player yaw, support, waterlogging, and paired-block rules: lever/button face + facing; repeater/comparator/observer/piston/dispenser facing; pressure plate/wire support; torch support; and door/trapdoor placement.
- Sources: lever, buttons, redstone block, redstone/wall torch, pressure plates, tripwire/hooks, target block, daylight detector, redstone ore, and sculk sensor output integration.
- Consumers: lamp, copper bulb, doors/trapdoors/fence gates, note block, and TNT only when primed-TNT entity behavior is available.
- Use scheduled ticks for reset/delay behavior. Pressure plates/tripwire need entity collision/movement refresh, not only neighbor updates.

### Existing behavior to preserve/integrate

- `UseItemOnPacket` first handles end crystals, buckets, note/jukebox use, a narrow container-only `interactableBlocks` list, portal items, then placement. Insert component use at the correct point so an empty-hand lever click does not become placement and sneaking retains vanilla placement semantics. The packet already has `Position`, clicked `Face`, float `Cursor`, `Hand`, and player yaw; use these for the placement-state helper rather than adding client guesses.
- `AbstractLevel.GameEvents.cs` activates sculk by setting `sculk_sensor_phase=active` and `power=1..15`, then uses a local `activeSculk` countdown (30 active + 10 cooldown). Replace only its power propagation responsibility; preserve vibration selection, block-entity frequency, cooldown, and event behavior.
- `AbstractLevel.Music.cs` exposes `PlayNoteBlock`; it checks the block above is air and emits sound/game events. Invoke it only on an unpowered-to-powered transition.
- `Entities/BreezeWindCharge.cs` currently directly toggles levers. Convert it to the same lever activation behavior, not a separate state mutation.

### Tests

Lever/button/plate/sculk to lamp/door/note; duration/reset timing; use packet handling; bulb and note rising edges.

## R5 — Redstone wire

### Work

- Share/extract the wire connection rules currently in `Features/Tree/ShapeUpdater.cs`.
- Recompute dot/line/cross and side/up wire connections on placement and relevant neighbor changes.
- Recompute `power` from valid sources, conductors, and wires; attenuate wire-to-wire by one; emit through valid sides only.
- Use correct queued propagation first. Optimize into component/network traversal only after profiling.

### Important wire details already present

`ShapeUpdater` has vanilla-derived helpers for support, climbability, dot/cross normalization, and connection rules. It uses horizontal order North/East/South/West, checks a conductor above before climbing, permits repeater connection only along its axis, and observer connection only from its face. Preserve these details when extracting shared code.

### Tests

15-block attenuation; vertical climb; dot/cross updates; source/repeater/observer connection rules; loops; chunk edges.

## R6 — Timers and logic

### Ready from R4 (use these, do not add parallel paths)

- Scheduled checks: `IRedstoneLevel.ScheduleBlockTick` (deduplicated per block type) and `IRedstoneLevel.EnqueueSimulation` (run on the simulation path). Register the callbacks in `BasicRedstoneComponents.RegisterTicks`-style code, next to the torch and lamp.
- Writes: `IRedstoneLevel.WriteAsync(position, block, notifyNeighbors)`. Use `false` where vanilla writes with flag 2.
- Replays: add the R6 scenarios to `ScenarioMatchesVanilla` by removing them from the `R4ScenarioNames` split and into the R6 list in `VanillaRedstone.cs`. `RedstoneTestWorld.Advance()` already runs block ticks in the level's order. `Reload` is still not implemented.
- Reuse `RedstoneEngine.HasSignal` for "is my input powered" queries (it matches vanilla `hasSignal`).

### Work

- Repeater: direction, 1–4 delay, side lock, scheduled state change, use interaction.
- Comparator: rear/side analog input, compare/subtract, mode toggle, output update.
- Observer: orientation-aware state detection and two-redstone-tick pulse without self-triggering.
- Torch: delayed inversion and the R0-selected burnout/recovery behavior.
- Add comparator providers only for backed systems: containers, furnaces, brewing stands, jukebox songs, etc. `Obsidian.API/_Types/JukeboxSong.cs` already carries `ComparatorOutput`; use that instead of hard-coded disc values. Use `IBlock.GetProperty`, `WithProperty`, and `IsSameState` from `Registries/BlockStateProperties.cs`; properties are immutable state transitions, not mutable block instances.

### Tests

Golden per-tick scenarios for repeaters, locks, comparator modes, observer pulse direction/duration, and torch burnout.

## Handoff gate

R7/R8 may rely on all power queries and scheduling here; they must not introduce a second signal/timing path.

## R6 progress (2026-10-09): repeater pass

**Done:** `Obsidian/WorldData/Redstone/RedstoneRepeater.cs` (new): repeater input/output (`facing` = input side, output opposite), `powered` after `2 × delay` game ticks via the persisted block tick, side lock by a powered repeater or comparator on either side (`locked` property), and use-to-cycle delay 1→4→1. Registered in `RedstoneRegistration.Install` alongside wire, sculk and basic components. Use cycling is in `BasicRedstoneComponents.TryUseAsync`; placement facing is derived in `BasicRedstoneComponents.DerivePlacementState` (player yaw, opposite of the input side).

**Design:** the scheduled tick is deduplicated per position and block name, so the pending output value is kept in a per-repeater map (`pending`). A tick that fires while locked drops its pending value. A tick that applies a turn-on while its input is already gone schedules the matching turn-off, so the output follows the input after the delay.

**Changed files:** `Obsidian/WorldData/Redstone/RedstoneRepeater.cs` (new), `RedstoneRegistration.cs`, `BasicRedstoneComponents.cs`, `AbstractLevel.cs` (removed a duplicate `using`), `Obsidian.Tests/VanillaRedstone.cs` (repeater scenarios split out of R6), `Obsidian.Tests/Repeater.cs` (new, 2 unit tests).

**Tests:** `obsidian_workflow build solution` succeeded. Focused `RepeaterTests` + `VanillaRedstone`: 29 passed, 1 skipped (`ScenarioMatchesVanillaAfterR6`). `repeater_delay` and `repeater_lock` both match the oracle at every checkpoint. `linux-smoke` (last full run, before the `using` cleanup and debug-test removal): 481 passed, 1 failed; the failure is the pre-existing `WorldPersistence.EntitiesAndContainersSurviveRestart`, unrelated to redstone.

**Superseded:** the open items listed here were completed in the observer, comparator and reload pass below.

## R6 progress (2026-10-09): observer, comparator and save/reload pass

**Status: all nine fixture scenarios now run as replays with no skips** (`VanillaRedstone`: 32 passed, 0 skipped). R6 is still not a handoff gate until the items under "Still open" are closed.

**Observer** (`Obsidian/WorldData/Redstone/RedstoneObserver.cs`, new). `facing` names the watched side; output is 15 on the back side (opposite `facing`) while `powered` is true. Oracle-derived timing (`observer_pulse`):
- Placing an observer schedules one pulse (vanilla `onPlace`): powered at the next tick, off two ticks later.
- A change to the watched block schedules a pulse, unless one is already scheduled. Nothing turns on at the change itself.
- A tick toggles the output. Turning on schedules the turn-off two ticks later.
- Placement facing is the clicked face (`BasicRedstoneComponents.DerivePlacementState`), which matches vanilla's look-direction rule when placed from that side.

**Comparator** (`Obsidian/WorldData/Redstone/RedstoneComparator.cs`, new). `facing` names the rear input side; the two perpendicular neighbours are the side inputs; output is on the opposite side.
- Compare mode outputs the rear value unless it is below the strongest side value, in which case 0. Subtract mode outputs `max(rear − side, 0)`.
- Any change in the computed output schedules a refresh two ticks later. The refresh commits `powered` when it changes, and notifies neighbours either way so analog changes reach them.
- Use toggles compare/subtract mode and schedules the refresh. Placement facing is the player's yaw, like the repeater.
- **Gap:** the analog output (0–15) lives in an in-memory map per comparator (`outputs`), where vanilla keeps it in the block entity. After a restart, an output reads as 15 when powered and 0 otherwise until its next refresh. Persisting it belongs with the R7 block-entity work.
- Not implemented: container, furnace, and other analog providers (the rear input reads only redstone signals).

**Save and reload.** `RedstoneTestWorld.Restart()` stops and restarts every chunk's block ticks through the level's own `LevelBlockTicks.Track`/`ChunkBlockTicks.StopTicking` path. Ticks keep their remaining delays. `EngineScenarioWorld.Reload` calls it. **Gap:** the NBT write/read of `block_ticks` is not exercised by this replay. It is covered only by the existing region tests.

**Changed files (this pass):** `RedstoneObserver.cs` (new), `RedstoneComparator.cs` (new), `RedstoneRegistration.cs` (installs both), `BasicRedstoneComponents.cs` (observer and comparator placement facing, comparator mode toggle on use), `Obsidian.Tests/VanillaRedstone.cs` (observer, comparator and reload replays; the R6 skip placeholder is removed), `Obsidian.Tests/RedstoneTestWorld.cs` (`Restart`).

**Tests:** `obsidian_workflow build solution`: succeeded. Focused `VanillaRedstone` + `RepeaterTests`: 32 passed, 0 skipped. `obsidian_workflow test linux-smoke`: 484 passed, 1 failed. The failure is the pre-existing `WorldPersistence.EntitiesAndContainersSurviveRestart` (`Witch` where `Living` was expected).

**In-game checks:** the repeater was checked in game by the user and works. The observer and comparator are not yet checked in game.

**Still open for R6 handoff:**
- Comparator analog state persisted through the block entity, and the container/furnace/brewing/jukebox comparator providers (`Obsidian.API/_Types/JukeboxSong.cs` `ComparatorOutput`).
- Observer placement and pulse behaviour through `UseItemOnPacket` on a live `AbstractLevel` (the replay runs on the test world).
- Save/reload through the real NBT `block_ticks` round trip (see above).
- Repeater and comparator placement derivation through `UseItemOnPacket` (only the helpers are unit-tested).

**R4 items still open** (unchanged by this pass): tripwire and hooks, target block, pressure-plate entity coverage for item entities and projectiles.

## R4 pass (2026-10-09): pressure-plate entity sensitivity

**Done:** `Redstone/PressurePlates.cs` now follows vanilla `PressurePlateSensitivity`. Stone and polished blackstone plates are pressed by living entities only (players and mobs). Wooden plates and weighted plates are pressed by any non-spectator entity, including item entities and projectiles. Detection counts both sets in one pass (`PlateOccupancy`), and each plate reads the set that matches its sensitivity, both when pressing and when deciding whether a release tick re-arms.

**Verification:** the `EVERYTHING`/`MOBS` enum and the weighted plate's `Entity` class with the spectator and block-trigger filters were checked in the 1.21.11 bytecode. The mapping of stone to `MOBS` and wood to `EVERYTHING` comes from vanilla's `BlockSetType` source; the bytecode search for the `BlockSetType` constructor calls did not finish, so it is not bytecode-confirmed.

**Changed files:** `Obsidian/WorldData/Redstone/PressurePlates.cs`, `Obsidian.Tests/RedstoneEngine.cs` (new test `StonePlateIgnoresNonLivingEntitiesButWoodenPlateAcceptsThem`).

**Tests:** solution build succeeded. Focused `RedstoneEngine|VanillaRedstone|RepeaterTests|RedstoneTorch|DaylightDetector`: 124 passed. `linux-smoke`: 485 passed, 1 failed (pre-existing `WorldPersistence.EntitiesAndContainersSurviveRestart`).

**Gaps:** `isIgnoringBlockTriggers` is not modelled (no Obsidian entity sets it yet). Entity detection still runs once per level tick, not on movement.

## R4 pass (2026-10-09, second): sneak use with empty hands

**Done:** `UseItemOnPacket.BypassesBlockUse` implements vanilla's `useItemOn` bypass: a sneaking player bypasses block use only when holding an item in either hand. Before, `UseItemOnPacket` skipped component use whenever `Sneaking` was set, so a sneaking player with empty hands couldn't toggle a lever or press a button. Only the redstone-component gate changed. The interactable-block gate (chests and similar) still uses the old `!Sneaking` rule, which differs from vanilla for empty-handed sneak clicks; it's outside redstone and is left as a known gap.

**Changed files:** `Obsidian/Net/Packets/Play/Serverbound/UseItemOnPacket.cs`, `Obsidian.Tests/BlockUseBypass.cs` (3 tests).

**Tests:** solution build succeeded. Focused `BlockUseBypass|RedstoneEngine|VanillaRedstone`: 86 passed. `linux-smoke`: 488 passed, 1 failed (pre-existing `WorldPersistence.EntitiesAndContainersSurviveRestart`).

### Tripwire facts (bytecode, `eiz` = TripWireBlock, 1.21.11)

- `entityInside` (not powered, and the entity isn't ignoring block triggers): `updateSource` runs with that one entity.
- `updateSource` (`a(dwo,is,List)`): `powered` = any entity in the list that isn't ignoring block triggers. If it differs from the current `powered`, write the new state with flag 3, then run the neighbour update. If it's now powered, schedule a 10-tick tick.
- Scheduled tick (`a(eoh,axf,is,bgr)`): when powered, `updateSource` runs again with the entities in the wire's box. A powered wire with nobody in it releases on this tick.
- String linking (`a(dwo,is,eoh)`): from the wire's position, walks two horizontal directions (vanilla `iz.d` and `iz.e`, not yet mapped to compass names) up to 41 blocks while the next block is the same tripwire block. If it reaches a hook facing back, it calls the hook's update (`eja.a(...)`).
- Hook: `eja` = TripWireHookBlock; its `attached`, `facing` and `powered` properties are set from its neighbours in `a(dwo,is,eoh,ddm)`-style code. Not yet read.

Property names (from the `BlockStateProperties` mapping in `Obsidian.AssetGenerator/obj/vanilla/1.21.11/server.txt`): `ATTACHED`, `POWERED`, `DISARMED`, `FACING`. In `eja` (hook): `b`=FACING, `c`=POWERED, `d`=ATTACHED. In `eiz` (tripwire): `b`=POWERED, `c`=ATTACHED, `d`=DISARMED.

### Tripwire hook facts (bytecode, `eja` = TripWireHookBlock, 1.21.11)

- Placement (`a(dpu)`): default state with `ATTACHED=false`, `POWERED=false`. `FACING` is the opposite of the clicked horizontal face, taken from the first horizontal side whose state `canSurvive`.
- Output (`a(eoh,dvt,is,iz)` = `getSignal`): 15 when `POWERED`, otherwise 0. `getDirectSignal` returns 15 only on the `FACING` side when `POWERED`.
- `calculateState(level, pos, state, removing, notify, sourceDistance, sourceState)`:
  - `attached` starts at `!removing`. `powered` starts false.
  - Scan `i = 1..41` along `FACING` from the hook. If the block is a hook whose `FACING` is the opposite of the hook's `FACING`, record `wireLength = i` and stop. Otherwise, if the block is a tripwire, or `i == sourceDistance` (with `sourceState` standing in for that block): `powered |= !DISARMED && POWERED` on it; `attached &= !DISARMED` (only at `i == sourceDistance`, where it also schedules a 10-tick tick on the hook). Any other block clears `attached` and does not stop the scan.
  - After the scan: `attached &= wireLength > 1`, and `powered &= attached`.
  - Write `POWERED=powered` and `ATTACHED=attached`, flag 3. If not `removing`, write the hook's new state, and if `notify`, notify neighbours (below).
  - If `wireLength > 0`, write the far hook with `FACING` set to the opposite (flag 3) and notify it. Call its `calculateState` path.
  - If `attached` changed, set `ATTACHED` on each tripwire or hook between the hooks.
- Neighbour notification (`a(dzq,dwo,is,iz)`): notifies the hook's own position and the block one step opposite its `FACING`. The sound and particle helper (`a(dwo,is,ZZZZ)`) is not modelled; sound isn't tested.
- Hook `neighborChanged` (`a(eoh,axf,is,boolean)`): if the hook is attached or powered, recalculate.

**Tripwire facts (bytecode, `eiz`):**
- `updateSource` (`a(dwo,is,List)`): `powered` = any entity in the list that isn't ignoring block triggers. If it differs from the current `POWERED`, write the new state (flag 3) and notify neighbours. If it's now powered, schedule a 10-tick tick.
- `entityInside` (`a(eoh,dwo,is,cgk,chc,boolean)`): if not powered, run `updateSource` with that one entity.
- Scheduled tick (`a(eoh,axf,is,bgr)`): if powered, run `updateSource` with the entities in the wire's box.
- String linking (`a(dwo,is,eoh)`): from the wire, walk two horizontal directions (vanilla `iz.d` and `iz.e`) up to 41 blocks. If a hook is found, call its `calculateState` with `removing=false, notify=true, sourceDistance=distance, sourceState=the wire`. The direction-to-name mapping is not checked.
- Placement hook (`a(dwo,is,eoh,ddm)`): shears used on a tripwire set `DISARMED=true` (flag 260) and fire a game event.
- Removal (`a(eoh,axf,is,boolean)`): if not moving, run the string update with `POWERED=true`.

The hook and wire logic above is implemented in the tripwire pass below.

## R4 pass (2026-10-09, third): tripwire and hooks

**Done:** `Redstone/TripWire.cs` implements the tripwire (`TripWire`) and the hook (`TripWireHook`), from the bytecode facts above.
- Hook: `calculateState` (41-block scan, far-hook write, attachment and power rules, notifications, tick 10 on the source), placement with a sturdy support (refused otherwise), removal (`OnRemovedAsync`), break when its support stops being sturdy, and the signal (15 when powered; direct only on the facing side).
- Tripwire: placement connections from neighbours, horizontal connection updates on neighbour change, line update on placement and removal (removal runs with `powered=true`), the powered-state change and its line update, the 10-tick recheck, and entity contact.
- Entity contact: `TripWire.ContactsOf` uses the wire's shape (attached: y 1/16–2.5/16; detached: y 0–8/16) and each non-spectator entity's box. `AbstractLevel` runs it once per tick after plates; `RedstoneInstall` carries the wire to the level and the test world.
- Removal hook: `IRemovalBehavior` and `NeighborBehaviors.RegisterRemoved`. `BlockNotificationPipeline` calls it for the old state when the block type changes.

**Changed files:** `Obsidian/WorldData/Redstone/TripWire.cs` (new), `Obsidian/WorldData/Redstone/RedstoneRegistration.cs` (returns `RedstoneInstall`), `Obsidian/WorldData/AbstractLevel.cs` (`Tripwires`, entity pass), `Obsidian/WorldData/BlockNotifications/NeighborBehaviors.cs` and `BlockNotificationPipeline.cs` (removal), `Obsidian/Net/Packets/Play/Serverbound/UseItemOnPacket.cs` (hook refusal and tripwire connections on placement), `Obsidian.Tests/RedstoneTestWorld.cs` (`Advance(contacts)`), `Obsidian.Tests/TripWire.cs` (5 tests), `Obsidian.Tests/VanillaRedstone.cs` (`tripwire_line` replay), `tools/redstone/scenarios.py` and `generate.py` (entity events), `Obsidian.Tests/Assets/redstone_scenarios.json` (regenerated; the nine earlier scenarios are byte-identical in their checkpoints and actions).

**Oracle scenario `tripwire_line`:** two hooks on stone supports facing each other across three wires. An armor stand touches the middle wire from tick 2 and is killed at tick 8. Vanilla powers the wire and both hooks at tick 2, holds them through tick 11, and releases at tick 12 (the 10-tick recheck after the contact at tick 2).

**Finding:** the first oracle run summoned the stand with `NoGravity`, and vanilla never powered the wire, so a stationary entity with no gravity never triggers block contact. Removing `NoGravity` fixed it. The first replay also released one tick early, because the contact was applied before the tick's step, so the recheck was scheduled from the previous tick. The replay now applies contacts after the block ticks of each tick. The live level runs its entity pass in its own order, and that order gives the same delay.

**Tests:** solution build succeeded. Focused `VanillaRedstone|TripWire|RedstoneEngine|RepeaterTests|BlockUseBypass|BlockTicks|BlockNotifications`: 120 passed (including the `tripwire_line` vanilla replay). `linux-smoke`: 496 passed, 1 failed (pre-existing `WorldPersistence.EntitiesAndContainersSurviveRestart`).

**Gaps:**
- Shears disarming (vanilla `playerWillDestroy`) is not implemented. Breaking a wire with shears still breaks it.
- Entity contact is taken once per level tick from entity positions, not on movement (vanilla checks on movement). A contact can be seen up to a tick later.
- Hook placement uses the clicked face, then the player's yaw. Vanilla orders all looking directions by the look vector.
- Hook and wire sounds and particles are not modelled.
- Tripwire and hook breaks don't drop items through this path (not redstone-specific).
- Entity tests use the contact list, not real `IEntity` boxes; `ContactsOf` is only exercised through the level.

## R4 pass (2026-10-09, fourth): target block and arrow hits

**Done:** `Redstone/TargetBlock.cs` (new), checked against the 1.21.11 bytecode (`ein` = TargetBlock, `bgj` = Mth).
- Hit (vanilla `onProjectileHit`): if a target has no pending tick, set `power` (write with neighbour notification) and schedule its end. The end is 20 ticks for an arrow and 8 for other projectiles. A hit while a pulse is pending does nothing.
- Strength: the larger offset from the face centre of the two in-face axes (x and z on a top or bottom face; y and z on a west or east face; x and y on a north or south face). Power is `max(1, Mth.ceil(15 × clamp((0.5 − offset) / 0.5, 0, 1)))`. The ceiling was checked in the bytecode (`bgj.e(D)I` is a ceiling), so an offset of 0.25 gives 8, not 7.
- End: a tick sets power 0 (flag 3) if it is still nonzero.
- Signal: `getSignal` is `power` in every direction. The direct signal is the default 0, as in vanilla.
- Arrow (`Entities/Arrow.cs`): on a terrain hit, `EnteredBlockFace` gives the block the arrow entered and the face it came through, from the stop point (on a block boundary) and its motion. It calls `AbstractLevel.OnProjectileHitBlockAsync` before the arrow embeds.
- Scheduling query: `ChunkBlockTicks.IsScheduled` and `LevelBlockTicks.IsScheduled` (vanilla `hasScheduledTick`), exposed as `IRedstoneLevel.IsBlockTickScheduled`.

**Changed files:** `Obsidian/WorldData/Redstone/TargetBlock.cs` (new), `Obsidian/Entities/Arrow.cs` (`EnteredBlockFace`, hit call), `Obsidian/WorldData/AbstractLevel.cs` (`Targets`, `OnProjectileHitBlockAsync`, `IsBlockTickScheduled`), `Obsidian/WorldData/Redstone/RedstoneRegistration.cs` (`RedstoneInstall.Targets`), `Obsidian/WorldData/Redstone/IRedstoneLevel.cs`, `Obsidian/WorldData/BlockTicks/ChunkBlockTicks.cs`, `Obsidian/WorldData/BlockTicks/LevelBlockTicks.cs`, `Obsidian.Tests/RedstoneTestWorld.cs` (`Advance(contacts, arrowHits)`, `ArrowHit`, `Targets`), `Obsidian.Tests/VanillaRedstone.cs` (`target_arrow` replay, arrow validation), `Obsidian.Tests/TargetBlock.cs` (new, 12 tests), `tools/redstone/scenarios.py` (`ArrowEvent`, `target_arrow`), `tools/redstone/generate.py` (summon arrows, `arrows` JSON), `Obsidian.Tests/Assets/redstone_scenarios.json` (regenerated; the ten earlier scenarios are unchanged).

**Oracle scenario `target_arrow`:** a target on a stone floor with a lamp to its east. Arrow 1 is summoned at tick 2 with motion +0.6 x and enters the west face at (3.0, 64.5, 1.25): vanilla power 8 from tick 2 to 21, 0 at tick 22, and the lamp is lit from tick 2 and out at tick 26 (4 ticks after power is lost, as for the lamp). Arrow 2 enters at the centre at tick 5 while the pulse is pending: it does nothing. The replay injects each hit at the tick vanilla made it, and matches every checkpoint.

**Tests:** solution build succeeded. Focused `VanillaRedstone|TripWire|RedstoneEngine|TargetBlockTests`: 106 passed. `linux-smoke`: 511 passed, 1 failed (pre-existing `WorldPersistence.EntitiesAndContainersSurviveRestart`).

**Gaps:**
- The arrow's flight is not replayed on the Obsidian side. The replay injects the hit that vanilla made. The flight is covered by the entity code, not the oracle.
- Only the arrow's terrain hit reaches a target. Other projectiles (snowballs, ender pearls, fireballs, wind charges) don't hit targets yet, so the 8-tick pulse is unimplemented.
- The live `AbstractLevel` hit path is not exercised by a test; the replay and unit tests use `RedstoneTestWorld`. This belongs with the R4 live-level item.
- Vanilla `TargetBlock.onRemove` and the advancement trigger on a hit are not modelled.

## R6 pass (2026-10-09, reload fixtures and oracle determinism)

**Done:**
- `reload_target_pulse` (new fixture): a target hit at tick 2 (pulse due at tick 22), world saved and restarted at tick 10. The pulse's remaining delay survives the reload; the lamp follows it. The Obsidian replay matches all 27 checkpoints (`ReloadScenarioNames`).
- `save_reload_repeater` is unchanged in the fixture. Standalone fresh runs give the same values as the old fixture (repeater on at tick 8).
- Generator (`tools/redstone/generate.py`): each scenario now runs in its own fresh world (`<work>/scenarios/<name>/<attempt>`), and the box is cleared to air before setup. Earlier, all scenarios shared one world, so a reload's timing and an arrow hit depended on what ran before. Every scenario is generated twice, and the generator fails if the two runs disagree. A new force-loaded area settles for four ticks before setup (`SETTLE_TICKS`).

**Withdrawn: `reload_observer_pulse`.** Vanilla's observer timing varied by a tick between fresh runs. In one run the observer was on at tick 2 and in another at tick 3, for the same setup and action. The engine matches the tick-2 value. The scenario was removed from `scenarios.py` and the fixture rather than recorded with a value that changes between runs. The same variance appeared once in a full run of the unchanged `observer_pulse` scenario (on at tick 3), and the double-run check caught it. The cause is not found: the oracle's `tick step` returns when game time advances, and nothing in the oracle explains a one-tick shift. Open item: find the source of the observer variance before any observer timing fixture is trusted.

**Open oracle issue:** the full generation with the double-run check failed in three of four runs (on `observer_pulse`, `save_reload_repeater` and `target_arrow`, one at a time). Each failing scenario was reproducible when run alone (10 of 10 standalone runs of `save_reload_repeater`, 2 of 2 of `target_arrow`). The fixture on disk was assembled from validated per-scenario runs: the 11 earlier scenarios unchanged from the previous fixture, plus `reload_target_pulse`. A full regeneration may still fail the double-run check until the variance is fixed.

**Tests:** solution build succeeded. Focused `VanillaRedstone|TargetBlockTests|TripWire|RedstoneEngine|Repeater|BlockUseBypass`: 114 passed. `linux-smoke`: 514 passed, 1 failed (pre-existing `WorldPersistence.EntitiesAndContainersSurviveRestart`).

**Changed files:** `tools/redstone/generate.py` (fresh world per scenario, box clear, double-run check, settle), `tools/redstone/scenarios.py` (`reload_target_pulse`), `Obsidian.Tests/Assets/redstone_scenarios.json` (12 scenarios), `Obsidian.Tests/VanillaRedstone.cs` (`ReloadScenarioNames`).
