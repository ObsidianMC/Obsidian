# Block ticking plan (draft)

**Status:** Milestone 1 (crops) is implemented (commit 07b4f791: `dumpBlockRandomTickClasses`, `RandomTickClass`,
generated `GetRandomTickBehavior`, `RandomTickBehaviors.cs` with CropBlock/BeetrootBlock/TorchflowerCropBlock). The
"Farming completion" section below evaluates what is still missing and plans it. Other milestones are sketches.

**Farming: feature complete** for tilling, farmland moisture and reverting, planting (seed items place their crops from
`item_placed_blocks.json`; crops and stems place only on farmland, `PlacementSurvival`), and crop growth on farmland. Known,
accepted gaps: farmland trampling (`FarmBlock.fallOn`) and `pushEntitiesUp` (need an entity landing hook), axe/shovel/shears
`useOn` owners, a general `canSurvive` owner hook (placement checks cover crops and stems only), and a fixed-seed vanilla
probe golden for `FarmBlock`.

## Goal

Make vanilla 1.21.11 random ticks and scheduled block ticks owner-driven: each vanilla behavior is attached to the Mojang
class that overrides it, generated from the vanilla dump, and implemented by an explicit Obsidian counterpart. Replace
the hand-written block-ID dispatch in `Obsidian/WorldData/Growth.cs` (`KindOf`) over time. Saplings and the propagule are done: owned by `SaplingBlock`/`MangrovePropaguleBlock`, with the tree from the dumped `sapling_growers.json`.

Non-goals for this plan: fire spread, precipitation ticks, protocol changes, and any change to `MinecraftVersion`.

## Current state (evidence)

| Area | Where | What it does today |
| --- | --- | --- |
| Random-tick sampling | `Obsidian.AssetGenerator/Java/VanillaDumper.java:418-462`, `block_physics.json` `randomTicking` | Per-state `isRandomlyTicking` flag. Drives which sections are sampled. Correct and vanilla-defined. |
| Random-tick owners | none | Vanilla `randomTick` owners are not dumped. |
| Placement pattern (template) | `dumpBlockPlacementClasses` (`VanillaDumper.java:566`), `Assets.GetBlocks`, `BlocksGenerator.Blocks.cs:75,94`, `WorldData/PlacementBehaviors.cs`, `Plugins/BlockBehaviors.cs:29` | Owner class to generated binding to counterpart. Silent `null` when unmatched (no diagnostic). |
| Plant growth | `WorldData/Growth.cs`, `AbstractLevel.Growth.cs:103-130` | Saplings and propagule on random ticks; saplings are classified by their generated random-tick owner and grow the dumped `TreeGrower`. `KindOf` still switches on `BlockIds` for azaleas, crops and grass. Crops tick through their owners; the others are bone-meal only. |
| Plugin random ticks | `WorldData/BlockUpdates.cs:145`, `Plugins/BlockBehaviors.cs` | `IBlockBehavior.RandomTick` for plugin blocks with `RandomlyTicks`. |
| Scheduled ticks | `WorldData/Ticks/TickScheduler.cs`, `ChunkTicks.cs`, `BlockUpdates.cs` | Vanilla-style `LevelTicks` for blocks and fluids. Falling blocks and redstone schedule through state-keyed checks. |
| Lava random tick | `AbstractLevel.Growth.cs:199-` | Vanilla chances, no fire. |

Vanilla reference (verified against 1.21.11 bytecode, `CropBlock` -> `ebp`):

- `randomTick` (mapping `b`): if `level.getRawBrightness(pos, 0) >= 9` (sampled **at the crop position**, ambient
  darkness 0) and `age < getMaxAge()`, compute `speed = getGrowthSpeed(this, level, pos)`; grow one age (set with
  flags 2) when `random.nextInt((int)(25.0f / speed) + 1) == 0`. Exactly one random draw per qualifying tick; no draw
  when light or age fails.
- `getGrowthSpeed` (mapping `a`, **`static float getGrowthSpeed(Block, BlockGetter, BlockPos)`**): start at 1.0; for
  each of the 9 positions one below the crop (dx, dz in -1..1), add 0 for non-farmland, 1.0 for dry farmland, 3.0 for
  farmland with moisture > 0, dividing the contribution by 4 unless directly below. Then if the same crop block is
  present both east/west and north/south, or on any diagonal, halve the result.

Obsidian's `Growth.GrowCrop` is bone-meal only and does not implement the random-tick path.

## Design

### Data flow (mirror placement)

1. **Dump.** `VanillaDumper` gains `dumpBlockRandomTickClasses()`: for each block, walk from its class upward to the first
   class that declares `randomTick(BlockState, ServerLevel, BlockPos, RandomSource)`. Write
   `block_random_tick_classes.json` (block key to Mojang class). Blocks inheriting the base no-op are omitted.
2. **Read.** `Assets.GetBlocks` carries it as `RandomTickClass` on `Block`.
3. **Generate.** `BlocksGenerator` emits `GetRandomTickBehavior(registryId)` bound to `RandomTickBehaviors.<Member>`.
   Unknown owners and owners without a counterpart produce a generator diagnostic (new `OBSGEN` id, not `OBSPK`), not a
   default.
4. **Counterparts.** `Obsidian/WorldData/RandomTickBehaviors.cs`: one member per owner class. Owners may share an
   implementation only after parity is shown.
5. **Dispatch.** `BlockUpdates.RandomTick` (or a successor) calls the generated lookup first. Plugin blocks keep their
   existing path.

Each vanilla behavior is attached to an owner class. Block membership is never inferred from IDs, names, tags, or
shared properties (see AGENTS.md "Behavior ownership").

### Milestone 1: crops

**Scope.** Random-tick growth for `CropBlock` owners: wheat, carrots, potatoes, beetroots (all `CropBlock` subclasses or
the class itself, as dumped). Nether wart, torchflower crop, melon/pumpkin stems, and cocoa are out of scope and stay in
the known-gaps list unless the dump shows they share the same owner.

**Steps.**
1. ~~Dump `CropBlock.randomTick` and `getGrowthSpeed` with `obsidian-vanilla-parity`.~~ Done; light threshold, draw
   order, and growth-speed body recorded above. Remaining: confirm the age cap per subclass (beetroot overrides
   `getMaxAge` to 3) from the dump, and produce the fixed-seed probe output for the golden test.
2. Add the dump for `randomTick` owners (`block_random_tick_classes.json`) and confirm `CropBlock` appears for the four
   crops only.
3. Generate the association; add a `CropBlock` counterpart in `RandomTickBehaviors` that reads `getGrowthSpeed`
   from its own dumped data (no block-ID table).
4. Keep bone meal (`Growth.GrowCrop`) as is, or move it to the same counterpart. Do not change its behavior in this
   milestone.
5. Tests:
   - Generated association: each crop maps to `CropBlock`; a non-crop does not.
   - Golden: for a fixed seed and light, random-tick outcomes match a vanilla probe (sequence of draws and resulting age).
   - Authoritative flow: a planted wheat in a loaded chunk advances age through `TickChunkBlocks` and the block update is
     sent to the client through the existing block-change path.
   - Light below 9 does not grow.
6. Logging: one Debug line per growth, with position, block, and before/after age. No per-tick logs.

**Human check (unmodified 1.21.11 client).** Plant wheat on farmland in light level 9 or more, stand nearby, and watch
the stages change over several minutes. Compare with a fresh vanilla server. Expected log: one structured Debug line per
growth. Second player should see the same stage change.

**Exit criteria.** Build and tests pass per `obsidian-validation`; `git diff --check` clean; generated output inspected
with `obsidian-source-generation`; human check performed or explicitly listed as not done.

## Future milestones (sketches)

- **M2. Other plant random ticks:** nether wart, cocoa, sugar cane, cactus, bamboo, kelp, sweet berry bush, chorus
  plant and flower, cave vines, vines, and mushrooms (spread). Each as its own owner, reusing the M1 pipeline.
- **M3. Surface change:** grass and mycelium spread and decay, farmland moisture and drying, dirt to grass. Needs the
  nearby-block scan that vanilla does in each owner, not a shared helper with a block list.
- **M4. Weather and time blocks:** ice and snow melt (`IceBlock`, `SnowLayerBlock`), copper oxidation
  (`ChangeOverTimeBlock`), and leaves decay (`LeavesBlock`). Needs the light and neighbor helpers from earlier milestones.
- **M5. Fluid random ticks:** lava and water owners (fluid dump). Lava fire spread waits on flammability and
  `ignitedByLava`, which needs its own owner dump.
- **M6. Scheduled-tick owners:** dump `tick` owners; migrate the state-keyed checks in `BlockUpdates` (falling blocks,
  redstone components) onto owner counterparts, one at a time.
- **M7. Precipitation and random-sequence parity:** draw `tickPrecipitation` so the random sequence matches vanilla
  across a full chunk tick.
- **M8. Retire the rest of `Growth.KindOf`:** saplings are done (owner plus dumped grower). Still to do: azalea (`TreeGrower` hand-typed as `azalea`), crops' bone meal, and the grass and tall-grass switch, each needing its owner counterpart.

## Farming completion (evaluation and plan)

### Evaluation of the M1 solution

Good: the owner pipeline (dump -> `Block.RandomTickClass` -> generated `GetRandomTickBehavior` -> counterpart) works and is
generic. Gaps:

1. **Only `randomTick` is owner-driven.** Farming needs five more vanilla hooks on the same owners, none of which is
   dumped or dispatched: `FarmBlock.tick` (scheduled), `FarmBlock.randomTick` (moisture), `FarmBlock.updateShape`/
   `canSurvive`/`getStateForPlacement`, `FarmBlock.fallOn` (trample), and the **item** hook `HoeItem.useOn`.
2. **`FarmBlock` already appears in `block_random_tick_classes.json` with no counterpart.** The generator reports
   OBSGEN001 and falls through to `Growth.RandomTick` (no-op for farmland). Same for `SpreadingSnowyDirtBlock`, `SugarCaneBlock`,
   `StemBlock`, `NetherWartBlock`, etc. - the OBSGEN001 list is the M2/M3 backlog; keep it tracked, not silent.
3. **`CropGrowth.Farmland` reads `moisture` but nothing ever sets it**, so all farmland is dry (speed 1 instead of 3) - and
   without the FarmBlock owner farmland never hydrates or reverts.
4. **No vanilla item-use-on-block owner dispatch.** `UseItemOnPacket` has only the plugin `ItemBehaviors` path and
   hard-coded `ItemIds` checks (ender eye, flint and steel, bone meal, spawn egg). Hoe/axe/shovel/bucket/shears have no path, so tilling,
   stripping, path-making, and scraping/waxing do nothing. These are `Item.useOn` overrides: the owner is the item class (`HoeItem`,
   `AxeItem`, `ShovelItem`), not an item ID list.
5. **Scheduled-tick dispatch (`RunScheduledTick`) is state-keyed** (plugin, redstone, falling); `FarmBlock.tick` needs the M6 `tick`
   owner pipeline, at least for the owners needed here.
6. Wheat seeds/crop placement survival already uses farmland (`BlockSurvival`), so planting works; hydration is the missing piece.

### Design (all data-driven from Mojang owners)

**A. Generalise the owner pipeline** instead of one-off dumps. Replace `dumpBlockRandomTickClasses` / placement with a single
`dumpBlockOverrides` that, for a list of virtual hooks (`randomTick`, `tick`, `updateShape`, `canSurvive`, `getStateForPlacement`,
`fallOn`, `useItemOn`, `useWithoutItem`), writes `block_overrides.json` (hook -> block -> declaring class). The generator emits
`Get<Hook>Behavior(registryId)` per hook with the same diagnostics (unknown/duplicate/stale owner). Add the same for items:
`item_use_on_classes.json` (`Item.useOn` declaring class per item), `ItemsRegistry.GetUseOnBehavior`.

**B. Dump the data HoeItem/AxeItem/ShovelItem use, do not hard-code it.** In 1.21.11 `HoeItem.TILLABLES`
(`Block -> Pair<Predicate<UseOnContext>, Consumer<UseOnContext>>`), `AxeItem.STRIPPABLES`, `ShovelItem.FLATTENABLES`,
`ShovelItem`/`AxeItem` scrape/wax maps are static maps in code. A Java dump reads them by reflection into
`item_use_transforms.json`: `{ownerClass: {fromBlock: {to: state, extraDrop: item?, requiresAirAbove: bool}}}`. Verify by bytecode which
predicate each entry uses (`HoeItem.onlyIfAirAbove` versus `always`) and record it as a named enum, not a block list. Expected hoe:
grass_block, dirt_path -> farmland (air above); dirt -> farmland (air above), coarse_dirt -> dirt (air above, no drop beyond item),
rooted_dirt -> dirt + hanging_roots drop. Validate against the dump, do not trust this list.

**C. `FarmBlock` counterpart (`BlockBehaviors` for each hook).** Port from bytecode with the golden probe:
- `randomTick`: if not `isNearWater(level,pos)` and not raining-at(pos.above): if moisture>0 set moisture-1 (flag 2), else
  if no crop on top (`shouldMaintainFarmland`) `turnToDirt`. If near water and moisture<7 set 7. Verify exact draws (none expected).
- `tick`: `!state.canSurvive` -> `turnToDirt` (pushes entities up). `canSurvive`: above not solid (`FarmBlock` blocks fence gates/solid).
- `updateShape`: when the block above is solid (UP direction), schedule a tick; `getStateForPlacement` yields dirt when `!canSurvive`.
- `isNearWater`: `BlockPos.betweenClosed(pos.offset(-4,0,-4), pos.offset(4,1,4))` any `FluidState` that is in tag `minecraft:water`
  (fluid tag from `tags.json`, which is vanilla-defined data and so allowed).
- `fallOn`: trample with chance `fallDistance - 0.5 > random.nextFloat()` and mob-griefing rule -> `turnToDirt`.
- Also `SpreadingSnowyDirtBlock`/`dirt` in M3 only as needed here.
**D. Item use dispatch.** In `UseItemOnPacket`, after the block's own `useItemOn`/`useWithoutItem` and the plugin path, look up
`ItemsRegistry.GetUseOnBehavior(item)`; the `HoeItem` counterpart reads `item_use_transforms.json`, checks the predicate, plays
`item.hoe.till` sound, sets state, damages the stack by 1 (respecting creative/hand-break), sends a game event, drops extra items, and
acks. Do this behind `IItemUseOnBehavior` so Axe/Shovel are the next owners on the same data path. Respect adventure mode and the
`BlockPlaceEvent`-like cancellation used by the existing paths.

### Implemented (farming slice)

- `VanillaDumper.dumpBlockOverrideClasses` writes `block_random_tick_classes.json`, `block_tick_classes.json` and
  `block_update_shape_classes.json`; `Block.OverrideClasses` carries them and `BlocksGenerator` binds each hook from a table
  (`OwnedHooks`) to `RandomTickBehaviors` / `TickBehaviors` / `ShapeUpdateBehaviors` (OBSGEN001 info for unmatched owners).
  `LevelBlockUpdates.RunScheduledTick` and `UpdateShape` dispatch to them.
- `FarmBlockBehavior` ports `FarmBlock.randomTick/tick/updateShape` (moisture, drying, reverting, solid-above); `HoeItem` is
  `ItemUseOnBehaviors` (owner from `item_use_on_classes.json`; tillables, result, drop and air-above from
  `item_use_on_transforms.json`, read from `HoeItem.TILLABLES`'s lambdas), wired in `UseItemOnPacket`. Tests: `Obsidian.Tests/Farmland.cs`.
- Not done: `Block.pushEntitiesUp` and `FarmBlock.fallOn` trampling (need an entity landing hook), axe/shovel/shears owners, generated
  (rather than runtime-looked-up) item owner binding, a fixed-seed vanilla probe golden.

### Steps (original plan)

1. Parity probes (obsidian-vanilla-parity): `FarmBlock` {randomTick, tick, canSurvive, updateShape, fallOn, isNearWater,
   turnToDirt}; `HoeItem` {useOn, TILLABLES}. Record the exact bodies here.
2. Generalise dump + generator (A) with diagnostics; keep the existing two JSON files working until migrated.
3. Dump item transforms (B) and item `useOn` owners; read in `Assets`, generate `GetUseOnBehavior`.
4. Implement `HoeItem` counterpart + dispatch (D). Test: hoe on grass/dirt with air above -> farmland, durability -1, nothing when solid above.
5. Implement `FarmBlock` counterparts (C) with scheduled-tick support for the owner (the minimum slice of M6). Tests: moisture 0->7 within
   4 blocks of water; decays by 1 per random tick when dry; reverts to dirt when dry+uncropped, stays when cropped; blocks placed above
   revert via scheduled tick; fall trample; fixed-seed golden.
6. Add the crop test that wheat on moist farmland uses speed 3 (existing `CropGrowth` tests extended).
7. Update docs and `Growth` known-gaps.

### Human check (unmodified client)

Hoe grass: farmland appears, hoe loses 1 durability, sound plays. Dig water 4 blocks away: farmland darkens within ~ a minute of random ticks.
Remove water: lightens then reverts to dirt (empty) or stays (planted). Jump on farmland: reverts to dirt. Place a block on it: dirt.
Expected Debug lines: one per tilling, per moisture change, per revert (position, before/after).

## Risks and open questions

- Simple-name matching in the generator can collide across packages. Use the Mojang FQN for the lookup key.
- Crops and other plants currently sample light from `RawBrightness`; vanilla `CropBlock.randomTick` uses
  `getRawBrightness(pos, 0)` at the crop position (verified above). Confirm Obsidian's `RawBrightness` matches that
  semantic (max of sky and block light with ambient darkness 0) before reusing it.
- Section sampling already counts plugin blocks as randomly ticking; confirm this does not change vanilla worlds.
- The known-gaps entry for plant growth lists "Where to start"; update it when M1 lands.

## Validation checkpoints

- `.pi/skills/obsidian-validation/scripts/validate.sh changed` after each milestone step.
- Generated `RandomTickBehaviors` and `BlocksRegistry` inspected via `obsidian-source-generation`.
- A golden fixture under `Obsidian.Tests` tied to the vanilla probe output.
