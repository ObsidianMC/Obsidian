# Redstone: index and engineering rules

## Package order

| Package | Scope | Depends on |
|---|---|---|
| R0 | behavior contract and vanilla scenarios | — |
| R1 | persisted scheduled block ticks | R0 |
| R2 | live block-change/shape/notification pipeline | R1 |
| R3 | directional power engine | R1, R2 |
| R4 | basic sources, block use, simple consumers | R3 |
| R5 | redstone wire | R2, R3, R4 |
| R6 | repeaters, comparators, observers, torch timing | R1, R3, R5 |
| R7 | dispenser/dropper, hopper, rails, powered entities | R3, R6 |
| R8 | pistons and moving blocks | R1–R6 |
| R9 | special-block coverage audit | applicable prior packages |
| R10 | parity/performance/release gate | all intended packages |

Read the package document matching the assigned package:

- Foundation: [`01-foundation.md`](01-foundation.md)
- Core circuits: [`02-core-circuits.md`](02-core-circuits.md)
- Devices and pistons: [`03-devices-and-pistons.md`](03-devices-and-pistons.md)
- Parity and release: [`04-parity-and-release.md`](04-parity-and-release.md)

## Parity status (2026-10-09)

Overall: **not at parity.** Parity holds only for the behaviours covered by the 11 recorded vanilla scenarios (`Obsidian.Tests/Assets/redstone_scenarios.json`), each replayed against the live engine with no skips. Everything else below is unverified against vanilla or not implemented.

| Area | Status | Evidence | Open gap |
|---|---|---|---|
| Lever, dust attenuation | matches | `source_timing_lever`, `dust_attenuation` | wire network evaluator (R5); the notification cascade uses budget 10000 |
| Repeater delay and side lock | matches | `repeater_delay`, `repeater_lock` | none in fixtures |
| Comparator compare/subtract | matches | `comparator_compare_and_subtract` | analog output in memory, not block entity; container/furnace providers (R7) |
| Observer pulse | matches | `observer_pulse` | placement through `UseItemOnPacket` on a live level untested |
| Torch inversion and burnout | matches | `torch_inversion`, `torch_burnout` | none in fixtures |
| Tripwire and hooks | matches (one layout) | `tripwire_line` | shears disarm; contact checked once per tick, not on movement |
| Target block, arrow hits | matches (arrows only) | `target_arrow` | the arrow's flight is not replayed; other projectiles (8-tick pulse) missing |
| Save and reload | matches (repeater and target pending) | `save_reload_repeater`, `reload_target_pulse` | in-memory restart, not the NBT `block_ticks` round trip; observer pending reload withheld (vanilla timing varies, see the R6 pass) |
| Pressure plates | unit tests only | `RedstoneEngine.cs` | no oracle scenario |
| Buttons and levers used by a player | unit tests only | `BlockUseBypass.cs`, `RedstoneEngine.cs` | the oracle can't schedule a button release, so no fixture; live-level use untested |
| Unloaded chunks | contract only | R0 contract | no fixture |
| Multi-block clocks, simultaneous updates | not tested | — | R10 audit |
| Block-tick vs fluid-tick order in one tick | not tested | — | no fixture |
| Observer timing from a setup or placement change | vanilla unstable | `observer_pulse` (fixed on tick 2 in repeated runs) | a tick-3 outcome was recorded once in a full run; cause not found in the oracle |
| Pistons, quasi-connectivity | not started | — | R8 |
| Dispensers, hoppers, rails, containers | not started | — | R7 |
| Sculk, note block, jukebox regressions | not audited | — | R9 |

Changes to this table must come with a fixture or a stated reason.

## Core rules

1. Redstone uses a common, directional 0–15 power API. No ad-hoc block-to-block power calls.
2. Neighbor work is queued, coalesced, bounded, and deterministic; never recursive.
3. Commit a new block state before any shape, redstone, or neighbor logic reads it.
4. Never load or generate a chunk because a power query reaches its boundary.
5. Delayed block behavior uses persisted vanilla-style `block_ticks`.
6. Share wire/shape logic with `WorldData/Features/Tree/ShapeUpdater.cs`; do not maintain two implementations.
7. Keep all live world mutations serialized through the level simulation/update path.
8. Follow SOLID principles: keep scheduling, signal evaluation, component behavior, persistence, and transport/UI concerns separately responsible; depend on narrow interfaces/behavior registries rather than concrete level or component types; extend behavior through registration instead of editing unrelated switches.
9. At the end of **every** package, compile and run tests before handoff. Run `obsidian_workflow` build, `obsidian_workflow` test with `linux-smoke`, and the focused tests added/affected by the package. Report any pre-existing unrelated failures separately.

## Existing implementation landmarks

- `Obsidian/Registries/BlockPhysics.cs`: generated conductor/source/class metadata. `IsRedstoneConductor()` is state-specific; `IsSignalSource()` is block-wide; `BlockClass()` returns vanilla simple class names.
- `Obsidian.AssetGenerator/Java/VanillaDumper.java`: `dumpBlockPhysics()` already invokes vanilla state APIs and writes `Assets/block_physics.json`; extend this rather than inventing static classification tables.
- `Obsidian/WorldData/Fluids/`: scheduler and persistence design reference. `FluidTickScheduler`, `ChunkFluidTicks`, and `LevelFluids` already solve ordered, chunk-owned scheduled work.
- `Obsidian/WorldData/Redstone/IRedstoneLevel.cs`: the one capability set redstone behaviours use from a level (committed writes with or without notification, clocks, sky light, persisted block ticks, deferred simulation work). `AbstractLevel` and `RedstoneTestWorld` implement it; registration injects it, so behaviours never cast.
- Direction convention: every `direction` argument in `RedstoneEngine`/`IRedstoneBehavior` is the direction **from the receiver to the neighbour** (vanilla `SignalGetter`). Weak `GetSignal` and direct `GetDirectSignal` both use it. A source answers direct queries for the face the receiver lies on.
- Notification budget: `BlockNotificationPipeline.MaxWorkPerDrain` is 10000 (see `01-foundation.md`, R2). Wire power changes notify a second ring of neighbours, so the budget is the first thing to check if wires go stale.
- `Obsidian/WorldData/AbstractLevel.cs`: live state mutation and level tick. `GetBlockAsync` may load/generate; use `GetLoadedChunk(...)? .GetBlock(...)` for redstone queries that must not do either.
- `Obsidian/WorldData/AbstractLevel.GameEvents.cs`: current sculk sensor state changes; it currently writes `power` directly and must be routed into the common engine.
- `Obsidian/WorldData/Features/Tree/ShapeUpdater.cs`: source of existing dust connection rules; it is internal/worldgen-oriented.
- `Obsidian/Net/Packets/Play/Serverbound/UseItemOnPacket.cs`: player block use/placement; only a narrow `interactableBlocks` set currently sends `PlayerInteractEventArgs`.

## Current level tick order

`AbstractLevel.TickLevelAsync` advances time, sends pending chunks, runs `Fluids.Tick()`, portals, queued entity actions, end fight/spawning/entities, raids/game events, then region block updates. Redstone/block-tick ordering must be inserted once and documented in R0; do not assume the existing gravity-oriented region update stage has usable ordering.

## Generated assets and source-generation warning

Do not spend time rediscovering this build behavior:

- `Obsidian/Obsidian.csproj` sets `MinecraftVersion` to `1.21.11` and references `Obsidian.AssetGenerator` as a build dependency. The asset-generator project runs `GenerateVanillaAssets` after it builds.
- The generator is normally skipped only while `Obsidian/Assets/.generated-1.21.11` and `Obsidian/Assets/collision_shapes.json` are newer than the generator executable/Java inputs. Thus, editing `Obsidian.AssetGenerator/Java/VanillaDumper.java` intentionally causes asset regeneration on the next build.
- Regeneration requires Java 25+ and can download/use vanilla server data. It can produce a large diff across `Obsidian/Assets`, not just the file a worker intended to change. R0/R3 are the only planned packages likely to need it; other packages must not modify the dumper casually.
- After changing the dumper, regenerate through the normal build, inspect the entire `Obsidian/Assets` diff, retain only expected data changes, then run the build and focused tests.
- `Obsidian.csproj` passes generated asset JSON to `Obsidian.SourceGenerators` as `AdditionalFiles`, so asset changes can change generated C# and compiler behavior. Always rebuild after touching `Assets`, `Obsidian.AssetGenerator`, or `Obsidian.SourceGenerators`.
- `Obsidian/Generated/**/*.cs` is diagnostic compiler output (`EmitCompilerGeneratedFiles=true`) and explicitly excluded from compilation. Never hand-edit it.

## Required handoff and durable progress record

Before handing off, update the assigned phase document in `docs/redstone/` with a dated progress entry or its package status section. Record: completed/in-progress/deferred scope; changed files; APIs added; exact tick/update ordering; tests/commands run and their results; known parity gaps; generated-asset regeneration steps; and explicit next work/dependencies. This plan is the durable handoff record—do not keep essential progress only in chat, a commit message, or an issue.

Do not hand off until compilation and the required test suite have been run. Do not leave untracked core TODOs.
