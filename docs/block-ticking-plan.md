# Block ticking plan (draft)

**Status:** draft. Milestone 1 (crops) is the only one with a detailed design; no milestone-1 code exists yet (no
`dumpBlockRandomTickClasses`, no `RandomTickClass` on `Block`, no generated `GetRandomTickBehavior`, no
`Obsidian/WorldData/RandomTickBehaviors.cs`). The vanilla `CropBlock` bodies are now verified (see below), so step 1 of
milestone 1 is done. Later milestones are sketches to be re-validated when reached.

## Goal

Make vanilla 1.21.11 random ticks and scheduled block ticks owner-driven: each vanilla behavior is attached to the Mojang
class that overrides it, generated from the vanilla dump, and implemented by an explicit Obsidian counterpart. Replace
the hand-written block-ID dispatch in `Obsidian/WorldData/Growth.cs` (`vanillaGrowers`, `KindOf`) over time.

Non-goals for this plan: fire spread, precipitation ticks, protocol changes, and any change to `MinecraftVersion`.

## Current state (evidence)

| Area | Where | What it does today |
| --- | --- | --- |
| Random-tick sampling | `Obsidian.AssetGenerator/Java/VanillaDumper.java:418-462`, `block_physics.json` `randomTicking` | Per-state `isRandomlyTicking` flag. Drives which sections are sampled. Correct and vanilla-defined. |
| Random-tick owners | none | Vanilla `randomTick` owners are not dumped. |
| Placement pattern (template) | `dumpBlockPlacementClasses` (`VanillaDumper.java:566`), `Assets.GetBlocks`, `BlocksGenerator.Blocks.cs:75,94`, `WorldData/PlacementBehaviors.cs`, `Plugins/BlockBehaviors.cs:29` | Owner class to generated binding to counterpart. Silent `null` when unmatched (no diagnostic). |
| Plant growth | `WorldData/Growth.cs`, `AbstractLevel.Growth.cs:103-130` | Saplings and propagule on random ticks; `KindOf` uses `vanillaGrowers` keyed by `BlockIds`. Crops are bone-meal only. |
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
- **M8. Retire `Growth.KindOf` / `vanillaGrowers`:** once every plant has an owner counterpart, remove the ID tables.

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
