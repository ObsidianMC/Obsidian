# Redstone foundation (R0–R2)

Load [`00-index-and-rules.md`](00-index-and-rules.md) first. This package creates infrastructure; do not implement individual redstone components here.

## R0 — Contract and parity fixtures

### Status (2026-10-09): complete

**Update (2026-10-09, latest):** the replay harness runs every recorded scenario against the live engine with no skips: nine from R0 plus `tripwire_line` and `target_arrow`, which the oracle generator now records with entity and arrow events. The replay ticks through `RedstoneTestWorld.Advance()`, which runs the real `LevelBlockTicks`. The reload scenario runs through `RedstoneTestWorld.Restart()`.

**Completed:** the behaviour contract below; a headless-vanilla fixture generator (`tools/redstone/`); twelve recorded scenarios covering the required categories; fixture tests in `Obsidian.Tests/VanillaRedstone.cs`, with one `MemberData` group and comparison test per package (R4, repeater, observer, comparator, tripwire, target). The 2026-10-09 R4 and R6 passes in `02-core-circuits.md` have the details.

**Changed files (uncommitted on branch `feat/redstone-foundation`):**
- `tools/redstone/vanilla_oracle.py` (new): RCON client, server lifecycle, freeze datapack, tick stepping, block-state reading.
- `tools/redstone/scenarios.py` (new): the scenario definitions, with reasons in their descriptions.
- `tools/redstone/generate.py` (new): runs the scenarios and writes the fixture. Usage: `python3 tools/redstone/generate.py [--only NAME ...]`.
- `Obsidian.Tests/Assets/redstone_scenarios.json` (new, generated).
- `Obsidian.Tests/VanillaRedstone.cs` (new): fixture tests, plus `IRedstoneScenarioWorld` and `RedstoneReplay` (test-only; no production API).
- `Obsidian.Tests/Obsidian.Tests.csproj`: the fixture in both `None Remove` and `EmbeddedResource`.

**Asset regeneration:** none. `VanillaDumper.java` and `Obsidian/Assets` are unchanged, so the next build does not regenerate assets. Deviation from the plan's suggested path: the vanilla server jar is obfuscated (ProGuard mappings), so a Java level harness would have to construct `ServerLevel` through reflection. The harness instead drives a real headless server over RCON, which runs the vanilla code paths unmodified.

**Tests run:** `obsidian_workflow build tests` succeeded. Latest `obsidian_workflow test linux-smoke`: 358 passed, 1 failed. The failure is `WorldPersistence.EntitiesAndContainersSurviveRestart` (`Assert.IsType<Living>` gets `Witch`). It is pre-existing: it fails the same way with this branch's csproj change reverted. Focused `VanillaRedstone`: 21 passed, 1 skipped (`ScenarioMatchesVanilla`, R3). After moving the reload scenario off an unstable restart boundary, two consecutive full `generate.py` runs are byte-identical.

### Contract (decided)

- **Target:** vanilla 1.21.11, the version in `Obsidian.csproj`.
- **Parity target:** the vanilla server's behaviour as it runs, quirks included. Update-order effects and quasi-connectivity therefore count as in scope; QC has no fixture yet and is covered with pistons in R8.
- **Per game tick (fixture and replay order):** (1) reloads, if any: save, restart, and the recorded number of ticks (one, see below); (2) the tick's actions, in list order, as live block changes with neighbor updates (`/setblock` semantics), at the previous game time; (3) the game advances one tick and the work due in it runs; (4) the checkpoint is read. Setup blocks are placed like actions at tick 0, before checkpoint 0.
- **Level tick order (for R1 to implement once):** scheduled block ticks run in one stage of `AbstractLevel.TickLevelAsync`, before `Fluids.Tick()`. Within the stage they run in `ScheduledFluidTick.DrainOrder` order (due time, priority, subtick). The per-tick cap is proposed as vanilla's block-tick budget (65536); confirm when implementing R1. Leftover due ticks carry to the next tick. The relative order of block and fluid ticks inside one vanilla tick is not yet covered by a fixture.
- **Priorities:** vanilla `TickPriority` values, reused from the fluid scheduler.
- **Neighbor propagation:** bounded and non-recursive (rule 2). R2 sets the queue budget; R0 only requires that it exists.
- **Unloaded chunks:** queries never load or generate chunks (rule 4). An unloaded position reads as unpowered. Scheduled ticks stay with their chunk and resume when it loads. No fixture covers unloaded chunks yet.
- **Reload:** a save and restart keeps scheduled ticks and their remaining delays. The restart runs one game tick before the load-time freeze takes effect, so the fixture records one tick per reload. The reload fixture is inside spawn chunks and reloads at tick 3; a tick-4 reload varied by one checkpoint in repeated runs, so avoid placing the checkpoint boundary directly on that unstable case. The committed fixture was regenerated twice byte-identically after moving the reload.
- **Fixture file:** Oracle fixture semantics follow the fluid fixtures: palette rows per checkpoint, with full canonical states (every property, sorted).

### Vanilla facts found while building R0 (for R3 onwards)

- A repeater's `facing` names its input side: the input is at `facing`, and the output is on the opposite side. Comparators use the same convention (rear input at `facing`). Getting this backwards silently gives unpowered circuits.
- Repeater delay *n* is `2n` game ticks. An input change applied at tick `t` shows at checkpoint `t + 2n - 1`: delay 2 gives `t+3` (lever at tick 2, repeater powered at checkpoint 5), delay 4 gives `t+7`.
- A repeater facing into the side of another repeater is locked by it (`repeater_lock`). A redstone block beside the repeater does not lock it.
- A redstone torch re-checks its support only when a direct neighbour changes. Powering the support from beside it does not invert the torch. Changing the support block itself does: a redstone block as the support turns the torch off after 2 ticks.
- Torch burnout: toggling the support every 2 ticks burns the torch out around the 15th toggle, after which it stays off. Toggling every game tick never changes the torch, because the 2-tick check sees the state set back again.
- `setblock` of a stone button in the powered state does not schedule its release; only a player use does. Button use is therefore not represented in fixtures (gap for R4).
- `tick step` returns before the tick runs. Commands run between ticks, so the oracle waits until game time advances before reading state.

### Known parity gaps (current)

The full status table is in [`00-index-and-rules.md`](00-index-and-rules.md#parity-status-2026-10-09). The open items for the fixtures are:

- Pistons, moving blocks, and quasi-connectivity (R8).
- Dispensers, droppers, hoppers, rails, comparators reading containers (R7).
- Player use of buttons and levers: unit-tested only. The oracle cannot schedule a button release, so there is no fixture.
- Unloaded-chunk behaviour: the contract above is a decision and has no fixture.
- Multi-block clocks and simultaneous updates from several sources (R10 audit).
- Reload covers a repeater's and a target's pending ticks. Torch burnout timers, comparator and observer ticks, and tripwire pulses are not reloaded in any fixture. The observer reload fixture was withdrawn (see the R6 pass in `02-core-circuits.md`).
- Block-tick versus fluid-tick ordering within one tick.
- The NBT `block_ticks` round trip is not exercised by a replay; the replay reloads in memory.

### Next work

Done since: R1 (persisted block ticks, see below), R2, R3, R4 (tripwire, target), and R6 (repeater, observer, comparator, reload). All twelve recorded scenarios replay against the live engine with no skips. Parity is **not** complete: the open items are in the status table in `00-index-and-rules.md`. Next parity work, in order: find the cause of the vanilla observer timing variance (blocks the observer reload fixture), the other pending-state reloads, the NBT `block_ticks` round trip, the unloaded-chunk fixture, and the multi-block clock fixtures (R10).

### Work

- Document target version (currently generated vanilla 1.21.11), redstone tick order, priorities, queue budget, and unloaded-chunk policy.
- Decide and record whether strict Java quirks—especially quasi-connectivity and update-order exploits—are required.
- Extend `Obsidian.AssetGenerator/Java/VanillaDumper.java`, or add a Java harness, to output small deterministic redstone scenarios: setup, timed mutations, and expected per-tick states.
- Add `Obsidian.Tests/VanillaRedstone.cs` and `Obsidian.Tests/Assets/redstone_scenarios.json`, modeled on `VanillaFluids.cs`. Also edit `Obsidian.Tests/Obsidian.Tests.csproj`: add the JSON to both the `None Remove` and `EmbeddedResource Include` lists, or the test assembly will not contain the fixture.

### Initial fixture coverage

Source timing, dust attenuation, repeater delay/lock, comparator modes, observer pulses, torch inversion/burnout, save/reload. Piston cases belong to R8.

## R1 — Persisted scheduled block ticks

### Status (2026-10-09): complete

**Completed:** block ticks as a reusable scheduler, chunk-owned storage, vanilla `block_ticks` persistence, and one documented level tick stage. No block behaviors are registered yet, so due ticks are consumed without effect; behaviors arrive with R3 and later packages through `LevelBlockTicks.Register`.

**Changed files (uncommitted on branch `feat/redstone-foundation`):**
- `Obsidian/WorldData/BlockTicks/BlockTick.cs` (new): `TickPriority` (vanilla values), `SavedBlockTick`, `ScheduledBlockTick` with `DrainOrder` and `IntraTickDrainOrder`.
- `Obsidian/WorldData/BlockTicks/ChunkBlockTicks.cs` (new): per-chunk container; pending while generating, live once ticking; `Read`/`Write` of `block_ticks`.
- `Obsidian/WorldData/BlockTicks/BlockTickScheduler.cs` (new): the level scheduler, with `MaxTicksPerTick = 65536`.
- `Obsidian/WorldData/BlockTicks/LevelBlockTicks.cs` (new): tracking, tick stage, stale check, behavior registry, unload pruning; reads through `IBlockTickLevel`.
- `Obsidian/WorldData/Chunk.cs`: `BlockTicks` property. `Obsidian/WorldData/Region.cs`: `block_ticks` read beside `fluid_ticks`, and write.
- `Obsidian/WorldData/AbstractLevel.cs`: `BlockTicks` property (shares `Fluids.TickLock`), `Track` on chunk load, tick stage before `Fluids.Tick()`, `ScheduleBlockTick`, and the `IBlockTickLevel` reads (loaded-only).
- `Obsidian.Tests/BlockTicks.cs` (new): 15 tests.

**Decision:** the package lives in `WorldData/BlockTicks/`, not `WorldData/Redstone/`, because block ticks are generic (vanilla `LevelTicks<Block>`), as the plan allows. The fluid architecture is copied rather than made generic, to leave fluid behavior untouched in this package; unifying the two is a possible later cleanup.

**APIs added:** `AbstractLevel.ScheduleBlockTick(position, block, delay, priority)`; `LevelBlockTicks.Register(blockName, behavior)`. Both are internal.

**Verified against vanilla 1.21.11 (bytecode of `ServerLevel.tick`, obfuscated class `axf`):**
- Block ticks use cap 65536 (`LevelTicks.tick(gameTime, 65536, tickBlock)`); fluid ticks use the same cap. The R0 proposal is confirmed.
- Order inside the tick: block ticks, then fluid ticks.
- Guard: block and fluid ticks run only when the level is not in debug mode and the tick-rate manager `runsNormally()` is true. Obsidian has no debug or tick-freeze model, so this guard is not modelled (gap; same as fluids).
- `tickBlock` runs a tick only if the block at its position is still the same block type. Obsidian compares `UnlocalizedName`, which matches vanilla's `state.is(block)`, so state changes alone don't cancel a tick.

**Tick ordering (as implemented):** per level tick, (1) `BlockTicks.Tick()`: game time advances, chunks that tick and have due ticks are drained by due time, then priority, then scheduling order, up to 65536; leftover ticks carry to the next game tick; (2) `Fluids.Tick()`; (3) portals, entities and the rest, as before.

**Persistence:** `block_ticks` compounds use vanilla keys `i` (block name), `x`, `y`, `z`, `t` (delay relative to save), and `p` (priority). Ticks outside the chunk and ticks with an empty name are dropped on read. Saving a chunk and reloading it keeps the remaining delays.

**Tests run:** build of the solution succeeded (100 warnings, all pre-existing; none from these files). `Obsidian.Tests.BlockTicks`: 15 passed. `linux-smoke`: 373 passed, 1 failed; the failure is the pre-existing `WorldPersistence.EntitiesAndContainersSurviveRestart`. Live server: restarted with this build, reached ready, and ran ticks without new exceptions.

**Gaps (not in R1):**
- Unloaded-chunk policy is implemented as in the contract but not yet checked against vanilla. The R0 `block_ticks` fixture for unloaded chunks is still missing.
- Debug and tick-freeze guard (see above).
- The `Chunk` constructor's `status` argument is ignored (only `SetChunkStatus` sets it). This predates R1; tests call `SetChunkStatus` explicitly.
- Per-tick cap is 65536, but nothing yet schedules enough ticks to reach it; it is covered by a smaller-cap unit test only.
- Block behaviors and the stale-check against neighbor-driven state are R2/R3 work.

**Next work (done):** R2 uses `ScheduleBlockTick` and the state-write-before-notify order, and its tests drive `AbstractLevel` through `SetBlockAsync`.

### Work

- Implement generic block ticks analogous to fluid ticks: scheduled/saved tick records, per-chunk container, level scheduler, priority, and monotonic same-tick order.
- Add a block-tick container to `WorldData/Chunk.cs`.
- Read/write vanilla chunk `block_ticks` in `WorldData/Region.cs`, adjacent to `fluid_ticks`.
- Register/unregister complete live chunks; preserve relative delays through unload/reload.
- Execute due ticks in one documented stage of `AbstractLevel.TickLevelAsync` with a per-tick cap.
- Reject a due tick if its block was replaced or no longer matches its expected identity/state rule.

### Exact implementation references

- Copy the architecture—not fluid types—from `WorldData/Fluids/FluidTickScheduler.cs`, `ChunkFluidTicks.cs`, `LevelFluids.cs`, and `FluidTicks.cs`.
- Fluid tick ordering is already defined by `ScheduledFluidTick.DrainOrder`: trigger tick, priority, then monotonic subtick order. Reuse the same rule for blocks.
- Add `Chunk.BlockTicks` next to `Chunk.FluidTicks` in `WorldData/Chunk.cs`.
- Chunk loading calls `Fluids.Track(chunk)` in `AbstractLevel.GetChunkAsync` (around line 269); register block ticks in the same path. Chunk removal begins in `AbstractLevel.ManageChunksAsync`; mirror fluid untracking/packing behavior.
- Region NBT loads `fluid_ticks` around `Region.cs:578` and writes them around `Region.cs:736`; read/write vanilla `block_ticks` in the adjacent locations. Vanilla saved tick compounds use `i`, `x`, `y`, `z`, `t`, `p`; block `i` is the block registry name.
- `GetLoadedChunk` only returns generated loaded chunks. Do not call `GetChunkAsync` from scheduler/power paths merely to inspect a neighbor.

Expected new files belong under `Obsidian/WorldData/Redstone/` unless the worker establishes a broader reusable block-tick namespace.

### Tests

Due time; priority/subtick ordering; duplicate scheduling; stale tick cancellation; cap carryover; NBT round trip; chunk unload/reload. `Obsidian.Tests` has `InternalsVisibleTo`, so test internal scheduler/engine types directly rather than weakening production visibility.

## R2 — Live block-change and notification pipeline

### Status (2026-10-09): complete; basic in-game behaviour confirmed

**Ordering (as implemented, `AbstractLevel.SetBlockUntrackedAsync` with `doBlockUpdate`):** read the old state, commit the new state, clean incompatible block entities, then gravity scheduling (region, loaded neighbors only), enqueue the change, fluid and portal reactions, and drain the queue. The queue runs shape updates for the six neighbors first, then `neighborChanged` reactions, in vanilla's W/E/N/S/D/U order. The fluid path (`LevelFluids.SetBlock`) only enqueues, because it runs under the fluid lock; the level drains right after `Fluids.Tick()`.

**Changed:** `Obsidian/WorldData/BlockNotifications/` (new: `BlockChange`, `IBlockChangeWorld`, `NeighborBehaviors`, `LiveShapeView`, `BlockNotificationPipeline`); `AbstractLevel.cs`, `AbstractLevel.BlockNotifications.cs` (new, explicit `IBlockChangeWorld`), `Fluids/LevelFluids.cs` (enqueue, `ScheduleShapeFluidTick`); `WorldManager.cs` (idempotent `DisposeAsync`, see below); `Obsidian.Tests/BlockNotifications.cs` (new, 8 tests).

**Behavior changes:** two-argument `SetBlockAsync` now notifies neighbors, like vanilla's default `setBlock` (used by falling blocks, portals, withers, and API callers). Live shape updates now run for every notified change: torches, levers, buttons, and similar blocks drop without support, and wires reconnect. Gravity no longer loads chunks to read neighbors. Shape-driven removals clear the block without dropping an item, the same as the existing break path (gap).

**Behaviors:** `NeighborBehaviors` holds neighbor and placement reactions, and `RedstoneBehaviors` holds signal output. Both are populated by `RedstoneRegistration.Install`, which the level and the test world share. R3 added the engine and wire, R4 added the sources and consumers (see `02-core-circuits.md`).

**Bounded and coalesced:** a drain processes at most `MaxWorkPerDrain = 10000` items and drops the rest, counted in `DroppedWork`. Pending duplicates are coalesced; the queue is FIFO and deterministic. The limit was 1000 (vanilla's chained-update figure) until the R4 wire fix, which notifies a second ring of neighbors on every wire power change; at 1000 the dust replay dropped work and went stale. R5 should replace that cascade with a network evaluator and then lower the limit again.

**Shutdown fix (found in the same session):** the server crashed with `ObjectDisposedException` on stop, from a second `WorldManager.DisposeAsync` run (`Server.StopCoreAsync` and the host's service scope both dispose it), which flushed already-disposed levels. `DisposeAsync` now runs once and shares its task. One stop after the fix was clean. The crash was seen once in the log, so repeat the stop check.

**Tests:** build succeeded. `BlockNotifications`: 8 passed. `linux-smoke`: 381 passed, 1 failed, the pre-existing `WorldPersistence.EntitiesAndContainersSurviveRestart`.

**Gaps:** the pipeline has been checked in game for torch, lever, button, wire, lamp and door basics only. Unloaded chunk reads treat positions as void air. Dropped items on shape removal. Shape updates write through the public write path, so they are not yet batched.

### Work

- Refactor `AbstractLevel.SetBlockUntrackedAsync`: retain old state, write new state, clean incompatible block entities, then invoke dependent systems and neighbor notifications. Today it calls `ScheduleBlockUpdateAsync` and `BlockUpdateNeighborsAsync` **before** `c.SetBlock(...)` (around lines 357–385), which is invalid for redstone.
- Make mutation intent explicit enough to separate state write, broadcast, shape update, redstone enqueue, fluid/portal reaction, and neighbor notification.
- Extract reusable live shape behavior from `WorldData/Features/Tree/ShapeUpdater.cs`, particularly attached-block support and wire connection shapes. Keep worldgen callers working.
- Do not use `Region.TickBlocksAsync` / unordered `BlockUpdate` traversal as redstone propagation. `Region` stores updates in a position-keyed concurrent dictionary and iterates its keys; `BlockUpdate` only assigns a delay to gravity blocks. It may remain limited to gravity.
- Ensure placement, breaking, explosion, entity, fluid, command/plugin, and future piston paths use the shared mutation pipeline.

### Tests

Post-write state is visible to reactions; support removal updates attached blocks; live wire shape reconnects; repeated changes have deterministic bounded processing.

### Existing mutation routes to audit

- Player placement: `Net/Packets/Play/Serverbound/UseItemOnPacket.cs` calls `SetBlockAsync(..., doBlockUpdate: true)`.
- Block breaks: `Events/MainEventHandler.World.cs` calls the same path.
- Explosions, end-fight rebuilds, mob block damage, and game events also call `SetBlockAsync` in `WorldData`/`Entities`.
- Fluids bypass `AbstractLevel.SetBlockAsync` through `LevelFluids.SetBlock`; preserve its locking and arrange a safe redstone notification rather than calling async world code while the fluid lock is held.

## Handoff gate

R3 may start only when block ticks persist and a changed block is committed before redstone/neighbor code sees it.
