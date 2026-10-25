# Devices and pistons (R7–R8)

Load [`00-index-and-rules.md`](00-index-and-rules.md) and [`02-core-circuits.md`](02-core-circuits.md) first.

## R7 — Powered inventories, rails, and block entities

### Work

- Dispenser/dropper: rising-edge activation, persistent inventory lookup, item-action registry, sound/event/animation. Do not mark a block supported merely because it changes a state.
- Hopper: power lock, transfer scheduling, item pickup and adjacent inventory transfer, comparator output.
- Rails: powered, detector, and activator rail behavior integrated with minecarts.
- Add supported powered block entities such as bell and lightning rod; add command blocks only if command execution infrastructure exists.

### Exact repository facts and touch points

- `Events/MainEventHandler.cs` currently opens `Container(9)` for dispensers/droppers and creates containers for hoppers; opening is not powered behavior. Persistent inventories are retrieved through `IBlockEntity`/`DataBlockEntity` and `AbstractLevel.GetBlockEntityAsync`/chunk block entities.
- Existing storage/container interaction is in `MainEventHandler.OnPlayerInteract`; do not replace it when adding redstone activation.
- `AbstractLevel.SetBlockUntrackedAsync` and `LevelFluids.SetBlock` both remove generated `DataBlockEntity` data when a replacement block has a different `BlockEntityType`; moving/replacing blocks must preserve or intentionally transfer block entities according to vanilla rules.
- The current repository has no demonstrated dispenser action registry, hopper transfer implementation, or rail/minecart redstone implementation. Treat each as real prerequisite work, not a one-line powered-state change.
- `EntityType` contains TNT/minecart variants, but the discovered concrete entity coverage is limited (for example `ChestMinecart`); do not assume `EntityType.Tnt` means a primed-TNT entity implementation exists. R4 TNT and R7 rail scope must either implement the missing entity mechanics or record an explicit deferral.
- Comparator output must read the same saved inventory/block entity that the GUI changes.

Expected touch points: redstone behavior files, `Events/MainEventHandler.cs`, container/block-entity code, item/entity actions, and minecart/rail code.

### Tests

A dispenser/dropper fires once per rising edge; powered hoppers stop; detector rails output power; entity/block inventory state survives reload.

### Constraints

These systems have dependencies outside redstone. If an underlying feature is incomplete, record it explicitly in the coverage audit rather than faking a powered state.

## R8 — Pistons and moving blocks

### Status (partial)

Implemented and checked against the vanilla oracle (`piston_extend_retract`, `piston_sticky_pull`, `piston_obsidian_blocked`):
placement facing, extension and retraction through the simulation path, the structure planner (12-block limit, immovable and
destroyable blocks, slime and honey lines and branches), moving-piston block entities that advance over two ticks and place the moved
state, and the block-line moves with neighbor notification. Moving-piston entities persist their moved state, progress and flags.

Not yet implemented or not verified: entity pushing and honey carrying use full-cube approximations without vanilla's collision
clamp; the base's neighbor-removal reaction and creative head breaking; the drop path (`type 2`) is not oracle-checked; chunk-unload
interruption and save/load round trips of a moving piston are not tested; piston sounds are unverified; and no live client check
has been done.

### Work

- Implement a planner for facing, 12-block push limit, movable/immovable/destroyable rules, block-entity restrictions, and slime/honey adjacency.
- Add moving-piston state/entity handling: extension/retraction timing, collision shape, entity displacement, chunk unload/interruption, persistence, and cleanup.
- Implement sticky pulls.
- When a move completes, run the common live state/shape/redstone/fluid/portal update pipeline. Do not hand-edit neighbors.
- Implement quasi-connectivity only if required by the R0 contract.

### Exact repository facts and touch points

- Generated assets and structures already refer to `Material.Piston`, `Material.StickyPiston`, and `Material.MovingPiston`; `WorldData/Structures/JungleTempleStructure.cs` places sticky pistons and repeaters, but no runtime piston behavior exists.
- `BlockPhysics.BlockEntityType()` notes that moving pistons create block entities elsewhere; do not rely solely on normal `HasBlockEntity` classification for moving-piston persistence.
- `WorldData/Structures/StructureTemplate.cs` already treats `MovingPistonBlock` as dynamic shape. Keep this behavior compatible with any runtime moving state.
- Block movement cannot be implemented as a sequence of ordinary `SetBlockAsync` calls without an intermediate moving state: it would expose incorrect collisions and redstone updates. Add a dedicated transaction/planner and completion path that then uses the common notification pipeline.
- Verify state/property names against generated `Obsidian/Assets`/`BlockStateProperties`, rather than assuming old Minecraft names. `WithProperty` resolves valid generated states and is the required way to produce every moved or extended state.
- Entity ticking occurs before the current region block-update stage in `AbstractLevel.TickLevelAsync`; document where motion progress and entity pushing occur so collision state is coherent.

Expected touch points: new piston/moving-block implementation, block entity persistence, entity collision, `AbstractLevel` mutation path, and redstone engine.

### Tests

Normal/sticky extension and retraction; 12-block limit; immovable blocks; slime/honey movement; entity collision; save/reload/interruption.

## Handoff gate

R9 must audit these as real mechanics, including generated jungle-temple pistons—not merely powered block states.
