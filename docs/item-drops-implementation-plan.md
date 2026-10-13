# Vanilla block item drops: implementation plan

## Objective and scope

Replace Obsidian's current “drop the broken block's material” logic with Minecraft 1.21.11's block loot tables and the vanilla rules around them. Stone should normally drop cobblestone, grass blocks dirt, and leaves should roll for saplings, sticks, and sometimes apples rather than invariably dropping themselves. Silk Touch, shears, Fortune, correct-tool requirements, block state, explosions, and block-entity data must affect the results as vanilla specifies.

Make the implementation data-driven and version-aware: regenerate vanilla assets from the configured server jar; compile loot data to strongly typed C#; report unsupported data at build time. Separate **what drops** from **why a block was destroyed** and **how resulting item entities are spawned**. This plan focuses on item drops; experience, statistics, advancements, and specialized block behavior need separate hooks and tests where necessary for full breaking parity.

## Repository findings

- `Obsidian/Obsidian.csproj` targets Minecraft **1.21.11** and the solution targets .NET 10. Asset generation requires Java 25+.
- `Obsidian.AssetGenerator/Datagen/DatagenAssets.cs` combines only the `archaeology`, `chests`, `dispensers`, `pots`, and `spawners` loot categories. Mojang's generated `data/minecraft/loot_table/blocks/*.json` exists in the asset generator's work directory but is not imported. The current version contains **1,083 block tables**.
- `Obsidian.API/Obsidian.API.csproj` already includes `Assets/loot_table/*.json` as source-generator input. `Obsidian.SourceGenerators/Registry/LootRegistryGenerator.cs` compiles those assets into `LootTables.All` and category members, with `[LootType]`-based polymorphic dispatch and diagnostics for unknown types, properties, and references. The loot runtime in `Obsidian.API/Loot/` currently models container loot, not the complete block schema.
- `Obsidian/Events/MainEventHandler.World.cs` currently removes the block, runs special infested-block logic, creates **one** item of the original block's `Material`, and spawns it. This directly causes the reported bugs. Removing the block can also remove its block entity before loot can inspect it.
- `Obsidian/WorldData/AbstractLevel.Explosion.cs` independently drops each destroyed block's material, with an approximate external decay roll. Applying table explosion decay without removing that roll would double-decay drops.
- `Obsidian/Assets/block_physics.json` already has a per-state `requiresTool` array obtained from vanilla. `Obsidian/Registries/BlockPhysics.cs` exposes `RequiresCorrectTool(int state)`. Tool defaults and `correct_for_drops` rules are in `Assets/item_components.json`; item and block tags are in `Assets/tags.json`. Generated `IBlockState` has only `Id` in its interface; concrete states have properties, so generic loot conditions need generated property access.
- `Obsidian/Events/MainEventHandler.Loot.cs` keeps per-level `RandomSequences` for container loot in a private `ConditionalWeakTable`. Block loot must share an intentional, vanilla-compatible random-sequence policy rather than introduce a second inconsistent owner.
- `Obsidian.Tests/VanillaLoot.cs` demonstrates deterministic tests against vanilla container loot; use the same fixture approach for block drops.

## Target architecture

```text
vanilla jar / datagen
    → AssetGenerator: Assets/loot_table/blocks.json + existing blocks/items/tags/component assets
    → LootRegistryGenerator: generated LootTables.Blocks and typed object graphs
    → block drop service: harvest check + context creation + loot evaluation
    → destruction caller: player, explosion, physics, or other explicit cause
    → item spawner: zero or more ItemEntity stacks
```

No per-break JSON parsing or reflection, and no hand-maintained mapping of individual blocks to items. Preserve loot pool, condition, entry, function, and random draw order.

## 1. Import all vanilla block loot tables

1. Extend `DatagenAssets.Write` to combine `data/minecraft/loot_table/blocks` into `Obsidian/Assets/loot_table/blocks.json`, keyed as `minecraft:blocks/<name>` using existing deterministic `Combine`/`ToId` behavior. Update comments that call loot categories container-only.
2. Verify normal builds regenerate this asset from the configured jar; don't hand-edit the resulting JSON. Verify clean-build inclusion in `Obsidian.API.csproj` and `Obsidian.csproj` and check for all 1,083 tables.
3. Let `LootRegistryGenerator` generate `LootTables.Blocks.Stone`, `LootTables.Blocks.OakLeaves`, etc., plus all IDs in `LootTables.All`. Add collision/duplicate-ID checks and inspect generated assembly size and build time; if static initializer size becomes problematic, generate compact factories or split categories without abandoning compile-time validation.
4. Treat a missing vanilla table as **no loot**, not permission to drop the original block. Ensure block IDs map to `minecraft:blocks/<path>` by the actual block resource name, not `Material.ToString()`.

## 2. Cover the entire 1.21.11 block-loot schema

Use classes under `Obsidian.API/Loot` with `[LootType]`, as existing container loot does. Add JSON-to-C# emission cases for special encodings to `LootRegistryGenerator`. Supported constructs must include:

| Kind | Values in generated block tables |
| --- | --- |
| Entries | `minecraft:item`, `minecraft:alternatives`, `minecraft:dynamic` |
| Conditions | `minecraft:survives_explosion`, `minecraft:block_state_property`, `minecraft:match_tool`, `minecraft:any_of`, `minecraft:table_bonus`, `minecraft:inverted`, `minecraft:random_chance`, `minecraft:location_check`, `minecraft:entity_properties` |
| Functions | `minecraft:set_count`, `minecraft:explosion_decay`, `minecraft:copy_components`, `minecraft:apply_bonus`, `minecraft:copy_state`, `minecraft:limit_count` |
| Numbers | numeric constant, `minecraft:uniform`, `minecraft:binomial` |
| Apply-bonus formulas | `minecraft:ore_drops`, `minecraft:binomial_with_bonus_count` (`extra`, `probability`), `minecraft:uniform_bonus_count` (`bonusMultiplier`) |

Also model optional integer/float bounds, state-property dictionaries, item predicates (`items` or `minecraft:enchantments` component predicates with minimum level), nearby-block/location predicates, data-component identifiers, and dynamic-drop names. The present `location_check` examples examine the other half of tall grass and large ferns at Y±1; the two `entity_properties` examples have an empty `this`-entity predicate. Implement those semantics, but **reject unknown future predicate fields** rather than ignoring them.

Source-generator requirements:

- Map the JSON's snake_case and camelCase keys (including `bonusMultiplier` and `offsetY`) to explicit typed properties; do not use a permissive generic dictionary where a known schema exists.
- Resolve item, block, enchantment, tag, and component IDs at generation time. Preserve tag declaration order where it affects sets and deterministic output; use compact ID sets for membership checks.
- Preserve array, alternative-child, condition, and function order. Support optional bounds such as min-only/max-only `limit_count`.
- Extend `OBSLT...` diagnostics for unsupported discriminators, properties, predicate fields, formulas, references, components, and duplicate generated members; errors must fail the build, never silently return `default!` as an acceptable runtime result.
- Keep unrelated container-loot generation and fixture outputs unchanged.

Runtime semantics to verify against vanilla source/fixtures:

- `alternatives` expands only the **first successful child** in order; success is entry expansion, not “emitted at least one nonempty stack.”
- `any_of` short-circuits; `inverted` negates its nested condition; conditions and functions run in vanilla order.
- `survives_explosion` succeeds automatically without explosion context, otherwise uses vanilla's `1 / radius` draw. `explosion_decay` independently rolls for each item in a stack when explosion context is present.
- `table_bonus` indexes chance arrays using the tool's named enchantment level, clamped as vanilla does, and performs the correct random draw; Silk Touch and shears checks use the **actual held stack**.
- `apply_bonus` implements all three named formulas, their defaults, their integer arithmetic and their exact random-call sequence.
- `set_count`, `limit_count`, uniform and binomial providers use vanilla rounding/bounds. Validate `INumberProvider.GetInt`/`UniformNumber` for the new uses rather than assuming container tests cover them.
- `copy_state` merges requested properties into the `minecraft:block_state` item component; `copy_components` copies only explicitly requested components from the source block entity.
- `dynamic` invokes a named drop provider; `minecraft:sherds` yields cracked decorated-pot sherds in correct order and count.

## 3. Construct a complete, API-safe block loot context

Extend `Obsidian.API/Loot/LootContext.cs` or introduce a block-specific context builder with:

- the exact pre-destruction `IBlock` and its state ID;
- the pre-destruction tool `ItemStack` (or clearly specified empty-stack semantics for bare hands), including enchantments and component patches;
- pre-removal block entity or immutable component snapshot;
- origin/block position and breaking entity (`ThisEntity`);
- optional explosion radius, absent for ordinary breaks;
- an accessible neighborhood/block query for location checks;
- a delegate/interface for named dynamic drops;
- the existing single `IRandomSource` and optional luck.

Keep `Obsidian.API` independent of concrete `Obsidian.WorldData` classes: define narrow world/block-entity interfaces or callbacks in the API and implement adapters in the server. Prefetch adjacent states or provide a synchronous loaded-chunk view for location checks; never synchronously block an async world API during evaluation. Ensure snapshots are captured before `SetBlockAsync(..., Air)` removes block entities. Contexts are per generation, not shared mutable state.

Centralize per-level random-sequence ownership used by containers and blocks, seeded from `RandomState.ParseSeed(level.Seed)` where vanilla calls for sequences. Verify how vanilla picks random sources for player blocks and explosions in 1.21.11 before choosing `table.CreateRandom(0, sequences)` or another path. **Every loot random call** should use the same `IRandomSource` for that evaluation. Item-entity velocity randomness should not consume loot draws.

## 4. Generate property access for arbitrary block states

`block_state_property`, neighboring-block checks, and `copy_state` require properties by vanilla JSON name/value. Add generated metadata next to the existing block-state generation from `blocks.json`:

```csharp
bool BlockStateProperties.TryGet(IBlock block, string vanillaPropertyName, out string vanillaValue);
```

Use a compact lookup by state ID, switch, or precomputed property table; do not reflect over generated structs or allocate a per-break dictionary. Values must be vanilla strings (`"7"`, `"upper"`, `"true"`), not C# enum labels. Test integer, enum, boolean, and differently named properties (for example vanilla `persistent` versus a generated `IsPersistent`). Missing properties must fail a condition, not default to a value.

## 5. Model harvest eligibility and tool defaults

A table does not determine whether a block requiring an appropriate tool may drop anything. First consult `BlockPhysics.RequiresCorrectTool(stateId)`; then, **only for requiring states**, test the effective tool's `minecraft:tool` rules against the block. Generate ordered default tool definitions from `item_components.json`, including block IDs/tags, `correct_for_drops`, optional mining speed, damage per block, and other fields needed for block destruction. Compile selectors to efficient block-ID sets.

Layer actual `ItemStack` patches/removals over generated item defaults: a patched `ToolDataComponent` wins, removing the default means no tool component, otherwise use the generated default. Follow vanilla matching order and defaults for absent `correct_for_drops`; confirm first-match behavior with fixtures. A block not requiring a correct tool can still use its loot table with bare hands. Include tests for bare hands, wrong tool type, wooden/stone/iron/copper/diamond/netherite tier exclusions, shears, and customized tools. Keep harvest logic reusable, outside the event handler. Separately implement tool durability on successful breaks according to vanilla tool rules; snapshot the tool used for loot **before** damaging it.

## 6. Preserve block-entity components and dynamic outputs

Before block removal, snapshot the block entity and expose vanilla-equivalent components to loot. Implement a single adapter in `Obsidian` that translates supported saved/block-entity fields into the existing `Obsidian.API/Inventory/DataComponents` types. Reuse `BlockEntityNbt`/component codecs where possible; don't copy all NBT indiscriminately.

The current block tables request: `custom_name`, `item_name`, `tooltip_display`, `rarity`, `container`, `lock`, `container_loot`, `pot_decorations`, `banner_patterns`, `profile`, `note_block_sound`, and `bees`. Support all of them, including unresolved container loot, bee occupants and decorated-pot decorations, with defensive snapshots. Test shulker boxes, banners, player heads, beehives/nests, and decorated pots. Record and fix any missing storage/serialization support rather than silently dropping requested components.

## 7. Introduce a single block-drop service

Add `Obsidian/WorldData/BlockDrops.cs` or equivalent with a request specifying exact block, position, level/view, tool, breaker, block-entity snapshot, destruction cause, optional explosion radius, gamerule/drop-suppression state, and random source. The service:

1. Rejects air, creative/no-drop causes, disabled drops, and suppressed drops.
2. Checks harvest eligibility when appropriate (explosions have no harvest tool).
3. Resolves `minecraft:blocks/<block name>`; missing tables generate nothing.
4. Builds the loot context and evaluates the compiled table.
5. Returns **all** nonempty item stacks without spawning them, so tests and explosion code can consume or merge them.

A separate spawn helper creates one `ItemEntity` per stack, allocates unique entity IDs, positions and velocities drops as desired, and follows the level's threading/entity-queue rules. Do not encode block-to-item switch statements or one-item assumptions in callers.

## 8. Wire player breaking to the service

Refactor `MainEventHandler.OnBlockBreak` carefully:

1. Retain sequence acknowledgment, cancellation, rollback packet, block updates, destruction animation clearing, exhaustion and game-event behavior.
2. Before changing the block: snapshot original block state, tool, and block entity; obtain nearby state if needed by the table.
3. Handle special block destruction behavior not represented by item tables (notably infested blocks spawning silverfish without Silk Touch). Let the block table, not the existing manual `infested_*` substitution, select items. Preserve creative behavior.
4. Remove the block and process vanilla-allowed item drops through `BlockDrops`, spawning every resulting stack.
5. Apply tool damage after loot generation; do not let durability changes affect predicates already evaluated.
6. Ensure cancellations, creative mode, `doTileDrops`, and wrong tools generate no ordinary items.

Audit interactions with multi-block structures and container blocks. Do not mistake “one player broke a block” for permission to spill or duplicate a container inventory; some blocks keep contents in their item and others scatter them through block-specific behavior.

## 9. Unify other destruction paths deliberately

### Explosions

Refactor `AbstractLevel.Explosion.cs` to snapshot block and block entity before air replacement, pass explosion context to `BlockDrops`, and spawn returned items. Remove its manual block-material fallback and external `random.NextSingle() <= 1 / radius` decay wrapper; otherwise vanilla table conditions/functions would be applied twice. Preserve explosion selection, `mobGriefing`, and relevant source-specific explosion drop-decay gamerules (`mob_explosion_drop_decay`, `tnt_explosion_drop_decay`, etc.). Check vanilla's rules for explosion types and item-stack merging rather than applying an unconditional approximation.

### Support loss, fluids, mobs, and other block updates

Audit runtime block-to-air/replacement sites and classify them as drop-producing, silent, or specialized. In particular inspect support/shape updates, fluids, door/multiblock behavior, mob destruction, portals, scripted changes, and world generation. Honor `BlockUpdateFlags.SuppressDrops`; **do not** make generic `SetBlock` automatically drop items, since it is used for generation and silent changes. Introduce an explicit `DestroyBlockAsync(cause, context, flags)` on the level if that makes migration and duplicate-drop prevention clearer. Move only audited drop-producing callers to it. Preserve specialized behaviors and prevent the same block from generating drops twice during recursive neighbor updates.

### Gamerules and adjacent mechanics

Implement/verify `doTileDrops` and destruction-source gamerules at the caller/service boundary. Keep XP separate: block experience is not specified by block loot tables; `blocks.json` includes definitions such as `minecraft:drop_experience`. Tool wear, statistics, advancements, particles, sounds, exhaustion, and game events are also distinct results. Add explicit destruction hooks and clearly identify which are in scope for this delivery; do not label item-table parity as full block-breaking parity until those behaviors match.

## Tests and vanilla parity evidence

### Asset and generator coverage

- On clean generation, `blocks.json` loot asset has the same complete table-ID set as vanilla's datagen directory, and `LootTables.All` contains each table.
- Recursively inventory every discriminator, property shape, predicate key, bonus formula, dynamic name, and component ID in generated block tables. Assert all are supported; synthetic unknowns must raise actionable build diagnostics, never be ignored.
- Check stable deterministic output across builds, generated member collisions, and unchanged container fixture results.

### Deterministic pure-loot fixtures

For fixed states/tools/contexts/seeds compare **stack type, count, components, and order** with vanilla-produced fixtures, as `VanillaLoot` does for containers. Include at least:

- stone ↔ cobblestone, grass block ↔ dirt under ordinary and Silk Touch tools;
- oak leaves with bare hands, shears, Silk Touch, Fortune levels: leaves versus independent sapling/stick/apple rolls; non-apple leaf variants;
- diamond, coal, copper, redstone, lapis, and Nether ores for Silk Touch and all Fortune formula families;
- correct versus incorrect/absent tool, plus tool component patches/removal;
- immature/mature wheat and other crops, slabs, candles, snow layers and state-dependent stack counts;
- tall grass/large fern other-half location predicates;
- sea lantern, glowstone, melon for count limiting;
- no explosion versus seeded explosion survival/decay, including multi-item stacks;
- cracked decorated-pot dynamic sherd drops;
- bee nest/beehive, shulker, banner, skull and statue component/state copying;
- empty/no-loot tables.

Check random-call order with deterministic vanilla fixtures, not only distribution tests. Avoid brittle expectations based on assumptions about vanilla formulas: produce fixture outputs using the matching 1.21.11 jar where feasible.

### Integration tests

Exercise extracted destruction logic or real break events: cancellation does not destroy or drop; survival produces multiple distinct entities where needed; creative and `doTileDrops=false` do not drop; held tool snapshot survives tool damage; captured block entity remains available after removal; infested blocks behave correctly; `SuppressDrops` works for physics changes; explosions do not double-decay; IDs, placement, and spawned stack components are correct. Test support-loss behavior separately from player destruction.

## Rollout and acceptance criteria

1. **Assets and compilation:** import block JSON, extend loot models/generator, and require full schema coverage with zero generator diagnostics. Build; inspect generated output and build/assembly size.
2. **Core parity:** add context, state-property and tool metadata, implement conditions/entries/functions, and pass deterministic fixtures for stone, grass, leaves, ores, crops, and explosions.
3. **Player integration:** use the shared drop service in `OnBlockBreak`, remove the direct-material fallback, preserve events and special infested behavior, and pass event-level tests.
4. **Block entities/dynamic drops:** implement all requested component copies and sherd behavior with fixtures.
5. **Other causes:** migrate explosions and audited physics/destruction callers, respecting gamerules and drop-suppression flags.
6. **Hardening:** compare additional vanilla fixtures, measure memory/compile/startup/per-break allocations, resolve performance regressions without runtime JSON interpretation.

Validate using the project-local Obsidian build/test workflows where available: Java 25+ asset generation, solution build, targeted tests, then the Linux `linux-smoke` suite; inspect generated diffs. Keep known unrelated platform/test failures separate from item-drop failures. For manual playtesting, use the local server harness so status remains accurate.

**Definition of done:** all 1.21.11 block tables compile with complete semantics; player breaking and integrated explosions evaluate them with correct context; zero/one/many item stacks, Silk Touch, Fortune, shears, correct tools, state, component copying, and drop suppression match deterministic vanilla fixtures; there is no generic “drop the destroyed block's material” fallback in migrated item-drop paths. Document remaining adjacent XP/special-block and unaudited destruction behavior explicitly if it is not delivered at the same time.

## Progress log

### Milestone 1 — Assets and compilation (rollout step 1)

Scope: import all 1,083 vanilla block loot tables, extend the loot models and `LootRegistryGenerator` so every block-table construct compiles with zero `OBSLT` diagnostics, and keep existing container output unchanged. Runtime semantics for block-only constructs are deliberately **not** in this milestone; those types throw `NotSupportedException` naming the milestone that implements them, so nothing silently evaluates to a default.

Checklist:
- [x] 1.1 `DatagenAssets` imports the `blocks` category. `Obsidian/Assets/` is gitignored and regenerated by the asset generator (offline, ~13 s, from `obj/vanilla/1.21.11`); the committed change is `Obsidian.AssetGenerator/Datagen/DatagenAssets.cs`. A dry run also changed `block_sounds.json`, `creative_tabs.json` and `statistics.json`; those are unrelated to loot and were not copied into the assets.
- [x] 1.2 Loot models for the full block schema under `Obsidian.API/Loot`: `Conditions/BlockLootConditions.cs`, `Entries/BlockLootEntries.cs`, `Functions/BlockLootFunctions.cs`, `Numbers/BinomialNumber.cs`, `Predicates/LootPredicates.cs`. `alternatives` (first passing child) and `binomial` are fully implemented. The other block-only constructs throw `LootPending.Evaluation(...)` (milestone 2) instead of returning defaults.
- [x] 1.3 `LootRegistryGenerator`: `ItemSet` values from item ids/tags (hook in `EmitValue`), `OBSLT004` duplicate-member diagnostic (registered in `AnalyzerReleases.Unshipped.md`). Unknown discriminators/properties were already `OBSLT001`/`OBSLT002`. Synthetic-unknown tests for those are **not yet written**.
- [x] 1.4 `Obsidian.API` and `Obsidian.slnx` build with 0 errors and 0 `OBSLT` diagnostics. `LootTables.Blocks` has 1,083 members; `LootTables.All` includes all of them.
- [x] 1.5 `VanillaLoot` (9) still passes. New `Obsidian.Tests/BlockLootSchema.cs` (3) covers table count, identifiers, compiled `alternatives`, and the loud failure for unevaluated constructs. Full `Obsidian.Tests` run: 605 passed, excluding `Noise`, `Encryption` and `Compression` (known invalid on Linux or already failing per repo notes).
- [~] 1.6 Measured now: `Obsidian.API.dll` 5.69 MB; generated `LootTables.g.cs` 1.6 MB. **Baseline was not captured before the change**, so the size delta and clean-build time are unknown.

Notes:
- The harness (`.pi/scripts/obsidian_harness.py`) and the `.pi/skills/*/SKILL.md` files referenced by the project guidance are absent in this checkout, so validation used `dotnet build` and `dotnet test` directly. `linux-smoke` as a named suite could not be run.

Status: **complete with the caveats above.** Next: milestone 2 (block context, state properties, tool harvest, and the evaluation semantics for every `LootPending` construct).

### Milestone 2 — Core parity and player-break integration (rollout steps 2 and 3)

Scope: the runtime semantics for block loot (context, conditions, entries, functions, providers), harvest eligibility with
item tool defaults, a single drop service, and player breaking routed through it with the material fallback removed.
Explosions, block-entity snapshots and dynamic drops are **milestone 3** and still fail loudly (`LootPending`).

Evidence: vanilla 1.21.11 fixtures captured headless by `Obsidian.AssetGenerator/Java/BlockLootFixtures.java`, run by
`FixtureDumper` during asset generation (MSBuild target `GenerateVanillaAssets`). The output is
`Obsidian/Assets/block-loot-fixtures-1.21.11.json` (generated, gitignored, embedded as `Obsidian.Assets.block-loot-fixtures-1.21.11.json`).
Its inputs are `Java/block-loot-fixture-input.json`. Only cases marked `unsupportedHeadless` may be skipped; any other failure fails generation.

Checklist:
- [x] 2.1 Context: `LootContext` gained `Block` (pre-break state), `Tool` (empty stack for bare hands, null for absent),
  `ExplosionRadius`, `BlockProperty` (required by block-state and block predicates; missing reader throws) and `BlockAt`
  (prefetched neighbourhood). The API stays free of world classes; the server supplies the delegates.
- [x] 2.2 Conditions with vanilla semantics (checked against bytecode): `survives_explosion` (`nextFloat <= 1/radius`, no draw
  without a radius), `block_state_property`, `match_tool` (item set + enchantment levels), `table_bonus` (level-indexed chance,
  clamped, draw even at level 0), `location_check`, `entity_properties` for `this`, `any_of`, `inverted`.
- [x] 2.3 Functions: `explosion_decay` (one draw per item, survivors kept), `apply_bonus` with `ore_drops`,
  `binomial_with_bonus_count` and `uniform_bonus_count` (runs whenever a tool is present, bare hands included), `limit_count`
  (min+max clamp, min-only, max-only), `set_count`. `copy_state` and `copy_components` still throw (milestone 3).
- [x] 2.4 Providers: `binomial` (`<` comparison), `uniform`, constants. `alternatives` expands the first passing child.
- [x] 2.5 Harvest (plan step 5): `BlockDrops.CanHarvest` checks `BlockPhysics.RequiresCorrectTool` first; then the effective
  tool (patched component, else removed = none, else item default generated from `item_components.json` by `ItemToolDefaults`),
  first matching rule that sets `correct_for_drops` (vanilla `Tool.isCorrectForDrops`). Tags resolve at load (`BlockTags`).
- [x] 2.6 Drop service (plan step 7): `BlockDrops.ForPlayerBreakAsync` rejects air, `doTileDrops=false` and unharvestable
  blocks; resolves `minecraft:blocks/<name>` (missing table = no drops); prefetches the neighbourhood; evaluates with the
  level's shared random sequences (`LevelLoot`, now also used by containers); returns non-empty stacks without spawning.
- [x] 2.7 Player breaking (plan step 8): `OnBlockBreak` evaluates drops before removal, skips them in creative, removes the
  block, keeps the infested-block silverfish rule (skipped by Silk Touch), then spawns every stack. The
  `material`-based fallback is gone from this path. Ack, cancellation, packets, exhaustion and game events are unchanged.
- [x] 2.8 Tool durability after loot (plan step 8.5): done in milestone 3 (see below).

Test results (`dotnet test`, excluding Noise/Encryption/Compression as before): 659 passed, 0 failed, including:
- `BlockLootParity` (32 vanilla cases: stone, grass, oak leaves with bare hands/shears/Silk Touch/Fortune, all ore Fortune
  families, Nether gold, crops, snow, sea lantern/glowstone/melon limit, explosion radius 1/3/4, bedrock/no table, state
  properties, unchanged empty-stack handling), plus a logic test for `location_check` on tall grass.
- `BlockHarvestRules` (tier table, bare hands, removed tool component). These follow vanilla's tier tags, not captured output.
- `BlockBreakIntegration` (real `OnBlockBreak` on a real world and connected player): stone/pickaxe, bare hands, Silk Touch,
  creative, `do_tile_drops=false`, cancellation, grass/dirt.

Known gaps, stated plainly:
- Tall grass and large fern location checks are **not** fixture-verified: vanilla needs a live level, so the headless
  capture skips them. The logic test covers the predicate only.
- The Java capture uses a headless `ServerLevel`/`DedicatedServer` allocated without a server, with only the feature-flag
  members set (see the comments in `BlockLootFixtures.java`). This is enough for the level-free paths covered here.
- Harvest and integration expectations are from vanilla tier rules and the item defaults, not fixtures.
- Infested-block silverfish spawning is not covered by an integration test.
- Random order is checked with explicit seeds; the level-sequence path (seed 0) is implemented but not fixture-verified.
- `BlockStateProperties.GetProperty` is a dictionary lookup (no per-break allocation), not the generated `TryGet` that plan step 4 describes.
- The generator's synthetic-unknown diagnostic tests (plan "Asset and generator coverage") are not written yet.
- Still open for milestone 3: explosions (plan step 9), block-entity snapshots and `copy_components`/`copy_state`,
  dynamic drops (sherds), tool durability, and XP/statistics hooks. The harness and skill files named in the project guidance
  are absent in this checkout, so `linux-smoke` was not run.

Status: **milestone 2 done with the gaps above.** Milestone 3 follows.

### Milestone 3 — Block entities, explosions, durability (rollout steps 4 and 5, plus hardening of step 8)

Delivered:
- [x] 3.1 Block entity components (`Obsidian/WorldData/BlockEntityComponents.cs`): `copy_components` (source `block_entity`) reads
  the block entity before removal through `LootContext.BlockEntityComponent`. No block entity copies nothing, as vanilla does.
  Mapped: `custom_name` (`CustomName`), `container` (`Items`, slots up to the highest saved slot), `container_loot`
  (`LootTable` + `LootTableSeed` as `loot_table`/`seed`). Saved data for `lock`, `bees`, `banner_patterns`, `pot_decorations`,
  `profile` and `note_block_sound` throws `NotSupportedException` (not modelled). Component IDs no block entity supplies
  (`item_name`, `rarity`, `tooltip_display`) copy nothing.
- [x] 3.2 `copy_state` (`minecraft:block_state` component), applied only when the block is the listed one; missing properties are skipped.
- [x] 3.3 Explosions go through `BlockDrops.ForExplosionAsync`. The material fallback and the external `1/radius` wrapper are
  gone. Decay follows the cause gamerule (`mob_explosion_drop_decay` for mob-caused explosions, otherwise
  `block_explosion_drop_decay`), and the radius is passed only when that rule is on. Destroyed blocks are visited in random
  order, and same-item drops merge into a stack, like vanilla's explosion drops.
- [x] 3.4 Tool durability after loot (plan step 8.5): `CombatItems.HurtItem` (with Unbreaking) charges `damage_per_block` when the
  block's hardness isn't zero. Creative players don't wear tools. The stack is cleared when it breaks and the slot is sent to the client.
- [x] 3.5 Break handler: block-entity data the table can't yet produce no longer crashes the break. The block is removed,
  no drops are spawned, and the failure is logged as an error (`AbstractLevel.ReportUnsupportedLoot`). This is loud, not silent.
- [x] 3.6 Shared spawn helper `BlockDrops.SpawnDrop`, used by player breaks and explosions. It draws its velocity from the global random, not from loot.

Evidence:
- The generated `block-loot-fixtures-1.21.11.json` has 35 cases. The new ones are bee nests with `honey_level=5` (silk touch versus
  bare hands), and a copper golem statue (`copy_state` of `copper_golem_pose`). The fixture records `block_state` as
  `{name=value}` on each stack, and the capture script reproduces it.
- `BlockDestructionIntegration` (11 tests): wear on break, no wear in creative, no wear on zero-hardness blocks, shulker
  contents and custom name copied onto the drop, unopened shulker loot table copied, named chest, unmodelled beehive
  bees (silk touch) dropping nothing and still breaking, infested stone spawning a silverfish after a world tick (none
  with Silk Touch), and explosion drops from the loot table (bedrock has none; non-decaying explosions keep every drop).
- Full suite: 677 passed, 0 failed (excluding Noise, Encryption and Compression, as before).

Not delivered (remaining gaps, stated plainly):
- `dynamic` (`minecraft:sherds` for cracked decorated pots) isn't evaluated; a cracked pot logs an error and drops nothing.
  Pot decorations (`pot_decorations`) aren't mapped either.
- `copy_components` for `lock`, `bees`, `banner_patterns`, `profile` and `note_block_sound`; these throw until modelled.
  Break-time drops for beehives, banners, player heads and locked containers are therefore missing when they hold such data.
- Container contents don't spill when a chest or barrel breaks. This was already missing before the change (the old path
  never spilled), and it needs an audited container-destruction path (plan section 9).
- Support loss, fluid, mob and other block updates aren't audited, so they don't drop yet. Only player breaks and explosions use the service.
- Explosion `tnt_explosion_drop_decay` isn't used: Obsidian has no TNT entity yet.
- Explosions don't use the destroyed-block random sequence for their loot yet; loot uses the level's sequence, as for breaks.
  The shuffle uses the explosion random. Neither is verified against a vanilla explosion capture.
- XP, statistics, advancements and sounds for block breaks are unchanged and not part of item-drop parity.
- Generator diagnostic tests (plan "Asset and generator coverage") are still not written.
- Performance measurement (plan step 6) hasn't been done. Tool defaults are generated once on first use, and loot evaluation
  still allocates a context per break.

Status: **milestone 3 done with the gaps above.** The remaining work is dynamic drops and pot decorations, the remaining
block-entity components, container spilling, the update-source audit, and hardening.

### Milestone 4 — Block-entity components and dynamic drops (rollout step 4)

Delivered:
- [x] 4.1 Lock: the saved `lock` compound (vanilla's `LockCode` key, lowercase; the adapter previously read `Lock`, which never matched) is copied as `minecraft:lock`.
- [x] 4.2 Bees: each saved occupant becomes a `BeesDataComponent` entry. The entity type is the registry ID (`(int)EntityType`, as the tag checks use it), and `id` is removed from the entity data, as vanilla's `TypedEntityData` does.
- [x] 4.3 Pot decorations: the saved `sherds` list (back, left, right, front) becomes `pot_decorations`. Missing or empty sides are bricks. No component is copied when the list is absent or empty.
- [x] 4.4 Sherd dynamic: `minecraft:sherds` (the only dynamic name in the 1.21.11 block tables) emits the four sides in order, using the pot's `pot_decorations` (bricks when absent). Without a block entity it fails loudly.
- [x] 4.5 Profile: `name`, `id` (four big-endian ints) and `properties` are mapped. Any other saved key fails loudly. `IsComplete` is set when id and name are both present.
- [x] 4.6 Note block sound: the saved `note_block_sound` string.

Tests (`BlockDestructionIntegration`, `BlockLootSchema`): beehive bees, cracked pot sherds in side order, cracked pot without saved sides (four bricks), intact pot sides as a component, locked shulker, player head profile and note sound, and the sherd fail-loudly and intact-pot cases.

- [x] 4.7 Banner patterns: `minecraft:banner_patterns` from the saved `patterns` list (`pattern`, `color`). Layers are sent inline with the asset and translation key from vanilla's `banner_pattern` data (`Assets/banner_patterns.json`, 43 patterns, generated by datagen). The server has no banner-pattern registry IDs, so inline is the only available encoding. An undefined pattern or unknown color fails loudly.

Tests: `BannerKeepsItsPatternsOnTheDrop` and `BannerWithAnUndefinedPatternDropsNothingAndStillBreaks`. The cracked pot test checks sherd order through `BlockDrops.ForPlayerBreakAsync`, since spawned entities aren't kept in spawn order.

Not delivered:
- 4.8 Vanilla-captured fixtures for sherds, pot decorations, lock, bees, profile, note sound and banners. They're covered by integration and logic tests, not captured from vanilla. Capturing them needs the headless dumper to build block entities with saved data, which it doesn't do yet.
- 4.9 Container spill on break (carried over from milestone 3).

Status: **milestone 4 done except the vanilla fixtures in 4.8.** Milestone 5 (other destruction causes) is covered below. Milestone 6 (hardening) is not started.

### Milestone 5 — Other destruction causes (rollout step 5): audit and partial implementation

Audit of runtime block-to-air and replacement sites (`SetBlockAsync(..., Air)` / `DestroyBlock`), excluding world generation. Status shows what is handled now.

| Site | Cause | Status |
| --- | --- | --- |
| `MainEventHandler.World.cs` `OnBlockBreak` | Player break | Drops via `BlockDrops` (milestones 2–3) |
| `AbstractLevel.Explosion.cs` | Explosion | Drops via `BlockDrops.ForExplosionAsync` (milestone 3) |
| `UseItemOnPacket.cs` (gravity block under the placed block) | Falling-block placement | Silent: the block becomes a falling-block entity, not a drop. Not verified against vanilla. |
| `BlockUpdates.HandleFallingBlock` | Falling-block start | Same as above, silent |
| `AbstractLevel.MobBlocks.cs` (wither-skull structure) | Wither spawn structure removal | Silent (not verified against vanilla) |
| `Sheep.cs` (grass eating) | Mob | Not audited |
| `Silverfish.cs`, `Enderman.cs`, `Ravager.cs`, `Wither.cs`, `EnderDragon.DestroyBlocksAsync`, `BreakDoorGoal.cs`, `MobSpawner.Lifecycles.cs` | Mob destruction | Not audited. Vanilla drops for mob griefing need verification per mob. Silent for now. |
| `AbstractLevel.Eggs.cs` | Turtle eggs | Not audited |
| `Player.Portals.cs`, `LevelPortals.cs` | Portal frame and portal block | Not audited |
| `AbstractLevel.EndFight.cs` | End spike structure | Not audited |
| `WaterFluid.BeforeDestroyingBlock` (water flowing into a block) | Fluid replacement | Drops via `LevelFluids.OnBlockDestroyed` and `BlockDrops.SpawnDestroyedByFluidsAsync`, which run after the fluid tick. Verified in `WaterFlowing…` tests. Vanilla `WaterFluid.beforeDestroyingBlock` calls `Block.dropResources` (bytecode). Drops ignore doTileDrops, as vanilla's `getDrops` does. |
| `LavaFluid.BeforeDestroyingBlock` (lava flowing into a block) | Fluid replacement | Silent, as vanilla (`fizz` only; bytecode verified). Verified in `LavaFlowing…` test. |
| `FluidLevel.ExecuteShapeUpdate` → `DestroyBlock` (block loses support on a fluid-triggered shape update) | Support loss | Drops via the same queue unless `SuppressDrops` is set. Matches vanilla `Block.updateOrDestroy` → `destroyBlock(pos, (flags & 32) == 0, …)` (bytecode verified). **Gap:** in the live level, general block changes run neighbour updates only for falling blocks (`BlockUpdateNeighborsAsync`). A block that loses support to a player's `SetBlock` is not destroyed at all, so no drop path is reached. Not implemented. |

Delivered: water-flow and support-loss drops (queued from the fluid tick), with lava verified silent.

Deviations and gaps:
- Drops for fluid-destroyed blocks are evaluated after the fluid tick, so the neighbour reads used by location checks see the state after the fluid changed. Vanilla reads them at destruction time. Matters only for location-checked blocks next to a fluid change.
- The queue-to-spawn path (`LevelFluids` → `SpawnDestroyedByFluidsAsync`) has no end-to-end test: the test world doesn't track generated chunks, so fluids never tick there. Tests cover the fluid decision (`ScenarioWorld` records destroyed blocks) and the loot (`ForDestroyedAsync`).
- The support-loss test is missing. The path is reached only through fluid-triggered shape updates.
- The mob rows are not audited, so their drops are still missing. Explicit `DestroyBlockAsync(cause, ...)` is not introduced.
- XP is separate, as the plan requires; not implemented here.

Status: **milestone 5 partly done** (explosions, water and lava flow, fluid-triggered support loss). Mob destruction, live-level support loss, and the end-to-end queue test are open. Milestone 6 is not started.
