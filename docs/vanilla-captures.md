# Vanilla worldgen captures

The worldgen parity tests replay references recorded from vanilla. Each entry holds a case's inputs (seed, position,
noise...) and the values vanilla produced for them, and each entry is one test case. There are two fixtures while
Phase 5 of `docs/porting-26.3-plan.md` builds the 26.3 generator beside the legacy one:

- `Obsidian.Tests/Assets/vanilla_worldgen_26.3.json` holds 26.3 references, which `VanillaDensityFunctions` replays:
  noise samples (`noises`), the value of every noise router and aquifer function of each dimension's noise settings
  at block positions (`router`), and inline density functions at block positions (`functions`), for what no router
  reaches. 26.3 evaluates noise and density in single precision, so the values are floats and must match bit for bit;
  `functions` records them as `Float.floatToIntBits` in hex too (`bits`), since JSON numbers hold neither NaN nor
  infinities. `terrain` holds chunks at the merged terrain status, `materials` holds small, single-rule boundary
  cases, `aquifers` holds single aquifer positions and `carvers` holds carving masks alone.
  `VanillaTerrainGeneration` and `VanillaMaterialRules` replay those sections; see below. `biomes`, `biome_points`,
  `biome_search` and `spawn_attributes` cover Phase 5 step 4's biome sources and natural spawning.
- `Obsidian.Tests/Assets/vanilla_worldgen.json` holds the 1.21.11 references of the legacy generator, which
  `VanillaWorldGeneration` and `VanillaStructureGeneration` replay: noise router values, biomes, chunks after the
  surface, carver and decoration steps, full chunks, the spawn, light, structure piece lists, structure blocks and a
  treasure map. They can't be re-recorded on 26.3; Phase 5 replaces them section by section with 26.3 captures.

## Re-recording

```sh
dotnet build Obsidian.AssetGenerator -t:CaptureVanillaWorldgen
```

This rewrites every captured value in `vanilla_worldgen_26.3.json` in place, using the server of the Minecraft version
`Obsidian.csproj` targets (`MinecraftVersion`). Like the asset generator, it needs Java 25, and it needs internet
access the first time a version is downloaded. A run takes about a minute. It prints one line per case, either
`unchanged` or `CHANGED` with the old and new values, so `git diff` of the fixture shows everything that moved.

The underlying command is
`Obsidian.AssetGenerator capture-worldgen <minecraft version> <work directory> <fixture>`. The build target runs it with
`Obsidian.AssetGenerator/obj/vanilla` as the work directory.

To add a case, add an entry with its inputs to the fixture section and re-record. Any placeholder value works for the
captured fields, since the capture fills them in. The test picks the new case up without any code change. A new kind of
reference needs a section, a capture in the harness and a test.

## How it captures

`Obsidian.AssetGenerator/Captures/WorldgenCaptures.java` is a Java probe, run from source against the server jar by
vanilla's own names (the jars aren't obfuscated since 26.1). It is not an asset input, so editing it doesn't regenerate
the assets.

For each seed in the fixture, with structures off and then on, `WorldgenCaptures.cs`:

1. Starts a headless vanilla dedicated server in-process, in a new world with that seed and structure setting
   (`server.properties` in a scratch folder under the work directory). It saves the world and exits. Picking the spawn
   generates chunks around it, so
2. it deletes the world's chunks (`region`, `entities` and `poi`), then
3. starts the server again on the saved world and runs that world's cases, each as a task on the server thread.

Nothing is reimplemented:

- **Noises:** `RandomState.create(noises, seed, overworld settings).getOrCreateNoise(noise).get(x, y, z)`.
- **Router values:** `RandomState.create(noises, seed, settings)` for the case's noise settings, then
  `sampleBlockValueUncached(function, x, y, z)` for each function of its `noise_router` and `aquifers`.
- **Functions:** `DensityFunction.CODEC` parses the case's `function` with the server's registries, then
  `sampleBlockValueUncached(function, x, y, z)` samples it with the overworld's `RandomState`.
- **Terrain:** the dimension's chunk source generates a chunk to `ChunkStatus.TERRAIN`, with structures disabled.
  A separate empty protochunk runs vanilla's own fill, material and carver methods, sharing one `NoiseChunk`. The
  probe checks that this isolated run produces the same blocks as the actual terrain status before recording it.
- **Materials:** `MaterialRule.CODEC` parses a rule or reference. Vanilla's material context supplies the captured
  biome, column depths, water height and surface gradients, and the rule evaluates one block. These cases exercise
  depth and height boundaries, gradients, biome conditions, 2D/3D noise thresholds, sequence fallthrough, block
  properties, terracotta bands and the ore rule's acceptance/filler/raw-ore paths. Others, at other positions and
  on a second seed, take both outcomes of `hole` (surface depth 0 and 3) and `above_preliminary_surface` (one block
  below and at the minimum surface), and of `temperature` one block either side of the altitude where taiga and
  windswept hills turn cold, and on and off a warm patch of the frozen-ocean modifier.
- **Aquifers:** a `NoiseChunk` for the chunk containing an overworld position runs its aquifer's `computeSubstance`
  with the case's density, which records the block (or `null` for a barrier) and `shouldScheduleFluidUpdate`. Then
  `computeFluid` records the fluid status of the position as an aquifer center.
- **Carvers:** the terrain capture's carving mask and carver sources alone, for a chunk that needs no blocks.
- **Features:** one placed feature on layered terrain. Every chunk of a features-step region is a protochunk at the
  terrain status holding the case's `layers` and `biome`. Vanilla's own `WorldGenRegion` (given the fields its
  constructor would set, since the chunks have no generation holders), `FeaturePlacer.placeWithBiomeCheck` and
  decoration random place it, and the changed blocks of the writable 3x3 chunks are recorded.

## Terrain references

On seed `12345`, the twelve overworld locations cover ocean and flooded caves `(0, 0)`, iron veins and underground
fluids `(-10, 0)`, meadow/deep dark `(30, 40)`, frozen ocean `(100, -100)`, stony peaks `(-44, -16)`, snowy slopes
`(-24, 48)`, eroded badlands `(-304, -280)`, desert/river `(-140, 28)`, sulfur caves `(56, 68)`, swamp
`(-352, 248)`, whose material rules place water that vanilla marks for post-processing, mangrove swamp `(-101, -92)`
and dappled forest `(40, -45)`. Nether cases cover wastes `(0, 0)`, warped forest `(20, -30)`, basalt deltas
`(-16, 16)`, soul-sand valley `(-24, 4)` and crimson forest `(-3, 3)`. End cases cover the central island `(0, 0)` and
outer islands `(80, -60)`. On a second seed, `-3141592653589793238`, they cover the overworld's `(0, 0)` and a swamp
`(-84, 33)`, and a Nether crimson forest `(2, -2)`.

Each case records these independent boundaries:

| Field | Reference |
| --- | --- |
| `densityData`, `density` | Every final-density float bit and its SHA-256, evaluated over the noise settings' whole chunk volume with a neutral beardifier. |
| `base`, `baseHeightmaps` | Block states and both generation heightmaps immediately after density fill and aquifers. |
| `materials` | Block states after material rules, including ore veins, bedrock, deepslate, biome surfaces and surface extensions. |
| `carvingMask` | The union of the source biomes' carvers, generated by vanilla's carvers before changing any blocks. |
| `aquiferFluids` | Aquifer fluid levels and types at specified underground/surface positions, where settings have aquifers. |
| `blocks`, `heightmaps`, `status` | Every block state and both generation heightmaps at the actual terrain status. |
| `postProcessing` | Fluid post-processing positions, including duplicates, in section order and insertion order within each section. |
| `materialBiomes`, `carverSources` | Captured dependency inputs: block-level biomes and the ordered carver lists of the 17-by-17 source chunks. |

`minY` and `height` describe the whole dimension's build range. Blocks include the air above the Nether and End's
noise ranges; density and mask buffers cover the noise settings' generation volume. A block buffer holds a palette
of identifiers with sorted properties, plus gzip/base64-encoded, big-endian 32-bit palette indices in **z, x, y**
order. Density buffers use the same order and store float bits. Masks store 0 or 1 in that format. Biome buffers
include one extra Y at the top because surface building looks up the biome above the highest block. Block hashes
cover the compact palette JSON followed by the uncompressed index bytes. Heightmaps contain first-free Y values in
**z, x** order. A post-processing position is `(y - minY) * 256 + localZ * 16 + localX`.

The 26.3 implementation is `WorldData/Generators/Terrain/TerrainGenerator`. Its single `Generate` entry point fills
density, applies materials, then generates and applies the temporary carving mask. It accepts biome and carver-list
lookups explicitly. `Generators/Biomes/BiomeTerrain` now supplies them from the 26.3 biome source, while these
stage-isolation replays continue supplying vanilla's captured inputs.
`DensityCompiler` accepts a beardifier function; terrain defaults it to its neutral contextual value until structures
are implemented. None of this switches the live generator or changes its 1.21.11 data.

Chunk replays have `Category=Worldgen` and stay out of CI. The 79 small material-rule replays run in CI alongside the
density tests. Features, structures, the remaining Nether/End pipeline and performance work remain
for the later Phase 5 steps. The noise-input point-sampling allocations noted in #597 are unchanged.

## Biome and spawn references

The 26.3 biome pipeline lives in `WorldData/Generators/Biomes`, beside the live 1.21.11 pipeline. Its overworld and
Nether parameter lists are copied from `reports/biome_parameters/minecraft` to `Assets/worldgen_26_3/biome_parameters`;
they contain 7,594 and 5 entries respectively. The original report order and integer endpoints are retained. The
shared search tree keeps its legacy branching factor of 6 by default; the new source selects 26.3's factor of 19.
Stable sorting and the previous winning leaf on each thread decide equal-distance searches.

- `biome_search` records ordered searches through both complete preset lists and small trees with 1, 6, 19, 20
  and 40 entries. Preset targets visit every entry midpoint, seeded miscellaneous points and repeated earlier
  queries. Results identify the winning entry, even when several entries name the same biome. `targetsData`
  contains six big-endian 32-bit quantized climate coordinates per query; `resultsData` contains one winning
  entry index per query. Both are gzip/base64 buffers. Synthetic lists retain their inline parameter inputs;
  preset lists are obtained from vanilla's registry during capture and from the datagen report during replay.
- `biome_points` records all seven quantized climate coordinates and the selected biome at quart positions.
  It covers both terrain seeds and all three dimensions, negative/distant coordinates, the End's central-island
  boundary and its exact erosion thresholds with adjacent float values. Constant erosion inputs are evaluated
  by vanilla's own End source with a bound sampler.
- `biomes` records sections at actual `ChunkStatus.BIOMES`, at every terrain capture location (including sulfur
  caves and dappled forest), plus more End locations. `palette` and `data` encode every stored quart in ascending
  y, z, x order, with big-endian 32-bit palette indices compressed as above. Selection uses vanilla's climate
  volume path, at quart corners, and searches in section, x, y, z order. Those are distinct from buffer order.
  `spawnBiomes` separately records full-resolution block lookups over stored sections, in z, x, y order. Those
  can differ from the uncached point-climate `materialBiomes` at cave borders, and clamp Y to storage height.
- `spawn_attributes` records vanilla's resolution of default, dimension and biome maps. It covers ordinary and
  new overworld biomes, the deep dark's empty categories, Nether costs and the End, plus inherited dimension
  spawns, explicit empty categories, overridden costs, an empty override and a probability modifier.

`VanillaBiomes.ParameterSearchMatchesVanilla`, `ClimateAndPointBiomesMatchVanilla` and
`SpawnAttributesMatchVanilla` are small CI replays. `PluginModifiersApplyAfterDimensionAndBiomeAttributes`
compares plugin removal/unload behavior with the captured empty-category and inherited-map results. The
`VanillaBiomeGeneration` section, block-biome, stored spawn-position and complete terrain replays have
`Category=Worldgen`. They select biomes themselves, then compare the material inputs, final blocks and fluid
post-processing with the independent terrain captures.

`BiomeTerrain.Generate` fills BIOMES before terrain and supplies seeded full-resolution material lookups and
source-biome carver lists. Storage still represents the new terrain status with the internal legacy `carvers`
enum value, which already serializes as `minecraft:terrain`. It does not activate this pipeline on live worlds.

Natural spawning now reads the 26.3 environment attributes at each spawn or charge position, with dimension
defaults and biome overlays before plugin spawn modifiers. Vanilla marks `natural_mob_spawns` for full-resolution
biomes, so it uses the seeded border lookup over loaded sections; population probability uses the quart biome.
Count providers retain their distributions. The old flattened spawn tables remain for the legacy plugin API
during Phase 5, while spawning reads the resolved `BiomeSpawns.At` snapshot. Features, structures and the final
live-generator switch remain later Phase 5 work.

## Carver references

`carvers` cases record only `carvingMask`, `carverSources`, `minY`, `height` and `settings`, which
`CarvingMaskMatchesVanilla` replays together with the terrain cases. On seed `12345` they cover an overworld chunk
whose caves cross its edges and the region-file border at chunk x -1/0 `(-1, 3)`, one with a carver from the next
region file `(29, 28)`, a distant chunk near the world border `(1874988, -1874991)` and a Nether chunk carved up to
the mask's top Y (120) that also has a source in the next region `(-3, -2)`. On the second seed they cover an overworld
chunk with sources in two other regions `(31, 33)` and another Nether chunk reaching the mask's top `(-36, 30)`.

## Aquifer references

`aquifers` cases name a position, a seed and a density. They cover the aquifer centers whose fluid level depends on
vanilla scaling the spread noise by `10.0f` in float, `(-48572, -31, -7773)` and `(-48572, -19, -7774)` on seed
`12345`, and spread samples just beside a multiple of 3, where the level quantizes. They also cover lava-noise samples
just inside and outside `|0.3|`, lava aquifers, nearest-center ties (which favor the later center in search order) and
the global lava level at `y = -55` and `-54`, on seed `12345` and on a second seed. `AquiferFluidLevelsMatchVanilla`
replays their fluid statuses together with the terrain cases' `aquiferFluids`, and `AquiferSubstanceMatchesVanilla`
replays their blocks and scheduling decisions. They have `Category=Worldgen` too.

The 1.21.11 probe also captured lit/full chunks, structure pieces and blocks, the spawn and a treasure map (in git
history before Phase 5). The feature and structure steps adapt those captures to 26.3 as they need them.

## Feature references

`features` cases place one 26.3 placed feature without biome selection, so features can be verified while 26.3
biomes are built separately. Each case gives a `seed`, a chunk (`chunkX`, `chunkZ`), the decoration `step` and
`index` that seed its random (vanilla's `setDecorationSeed` and `setFeatureSeed`), a `biome` that lists the feature
(for its biome filter), and `layers`: block ids from the bottom up, each filling from the previous layer's top + 1 to
its own top, with air above. The capture records `placed` (the placement's result), `regionRadius` (vanilla's region
radius for the features step) and `changes`: `[x, y, z, state]` for each block of the writable 3x3 chunks that
differs from the layers, in z, x, y order.

`VanillaFeaturePlacement` replays them with `WorldData/Generators/Features/FeatureRegistry`, the 26.3 feature
machinery built beside the legacy features. It builds the layered chunks two chunks around the decorated one; the
features replayed so far read no further. Cases whose feature types aren't implemented yet are captured and
listed as pending in the test. Biome decoration (each biome's ordered steps) waits for 26.3 biome
selection. The replays have `Category=Worldgen`.

Trees and large features have their own block of named cases at the end of the section: each tree family (oak,
birch, fancy oak, spruce and pine, giant spruce and pine, jungle and mega jungle, dark oak, acacia, cherry, azalea,
swamp oak, pale oak without moss, poplars, mangroves on mud) with bees and leaf litter variants, fallen trees, huge
mushrooms, crimson and warped fungi (wild and planted) and spikes. Rejections cover blocked trunks, a blocked cap,
wrong substrates, and the checked trees' sapling-survival filters on sand. Pale oaks with moss wait for
`vegetation_patch`, which their moss decorator places.

Feature review regressions extend the same `features` section. A named case may provide an inline placed
feature, an explicit `origin`, a legacy `randomSeed`, and localized `edits` (`[x, y, z, block state]`). Edits
are part of the initial terrain and are excluded from `changes`. Registered-feature cases retain their
biome-filter identity. Every placement records `nextRandom`, including rejected placements.

Component cases use `probe: provider`, `modifier`, `predicate` or `rule`, with the corresponding inline
codec input. They record the optional state, ordered positions, or boolean result and the next random int.
Provider cases with `requiredState: true` instead call the required-state path. Empty `random_block` lists and
tags exercise both paths: optional reads return null, required reads preserve the existing state's properties,
and neither consumes randomness.
`FeatureComponentMatchesVanilla` replays these independently of feature placement. The cases exercise
nested and fixed-direction rotated providers, float noise count boundaries, integer-provider short circuits,
26.x support-sensitive survival, modifier termination and thresholds, rule matching, and height limits.

Moss carpet cases seed the region random independently with `regionRandomSeed` and record its next value
as `nextRegionRandom`; the placement random is checked separately. They cover wall sides, toppers,
unsupported upper sides and blocked toppers. Disk cases with `postProcess` record ordered chunk/section
`marks` and `postChanges` after vanilla's neighbor-shape pass. These cases have no fluid or block-specific
post-processing. Replays run Obsidian's actual chunk post-processing and compare the resulting blocks.

`expectChanges: true` advertises a nonempty placement: the replay fails if either the captured or replayed
changes are empty. Existing rejection captures remain, with added native-feature successes for flowers,
pumpkins, mushrooms on mycelium, berries, water and lava springs with one lateral opening, and kelp.
Selectors, sequences, writable-region crossings, biome rejection and exposed-ore discard chances also have
small deterministic captures. Fixture setup edits never count as successful placement evidence.

The explicit simple-provider capture uses 26.3's `minecraft:simple`. Its `compatibilityProvider` additionally
replays Obsidian's existing `minecraft:simple_state_provider` alias against the same captured state and random
tail. Vanilla rejects that older type name. Random-chance captures use the current `chance` field.

The `non_tree_` cases form a separate block of 26.3 references. They cover scattered ore, lakes, dripstone and
sulfur speleothems and clusters, vegetation patches (including waterlogged interiors), neighbor spreading,
overlays, multiface growth, templates, replacement blobs, deltas, pillars, stepped columns, End islands,
gateways and chorus plants. Additional references cover block piles, freezing, coral branches, root systems
and the unchanged algorithms shared with the legacy generator. Actual vanilla entries exercise basalt
columns and pillars, glowstone, twisting and weeping vines, sulfur springs, desert wells and warm-ocean coral.
These references verify blocks, placement results and the next placement-random value; multiface and sulfur
template cases also verify the ordered post-processing marks. Multiface cases with `postProcess` additionally
verify the resulting neighbor shapes. Sulfur templates use `captureMarks` to record marks independently of fluid
ticks and shape processing; `expectMarks` requires a nonempty mark list.
Direct magma, soul sand and mushroom captures distinguish the position above bubble-column sources from a
mushroom's own position. Templates rely on the region's write-time marks rather than enqueueing duplicates.

The review captures cover exact template processor states (compact and property-bearing states, including
random exact-state rules), and End platforms whose origins lie just outside the writable region. A platform at
`(32, 0, 0)` overlaps the region in x=30..31; one at x=34 has no writable overlap but still returns true.

The `coverage_` cases add a second placement random seed (19) across twelve representative algorithms, water
lakes with observable freezing, air lake barriers, actual bamboo podzol, ceiling-only dripstone and sulfur
clusters, and root systems with water inside and just beyond their allowance. Lake shell predicates, unsupported
multiface growth and occupied cluster origins reject inside the algorithms, without an always-false placement
filter. `expectBlocks` pins the advertised branch outputs in both the capture and replay.

Eleven Nether and End cases select a vanilla `dimension` and record its `minY`, `height` and generator `seaLevel`.
The replay uses these values for its chunks and region: both dimensions build from 0 through 255, with sea levels
32 and 0 respectively. Existing cases without dimension parameters retain their Overworld defaults.
The decoration context separately uses the dimension's noise generator range, including the Nether's height of 128.

Provider references cover `noise`, its Obsidian compatibility alias `noise_provider`, `dual_noise` and
`random_block`. Large and negative coordinates distinguish float scaling from double scaling. Modifier
references cover `fixed_placement`, `count_on_every_layer`, `cuboid` and `randomly_selected`, including empty
outputs and count resampling. Predicate references add `height_range` and `volume_match` boundaries.

Template features require an explicit 26.3 template resolver. The replay extracts templates from the cached
26.3 server jar instead of using the legacy structure cache; NBT templates are not checked into the repository.
Processor lists are read from the 26.3 worldgen assets, adapting compact block states at the feature compiler
boundary before invoking the shared processor algorithms. These additions do not switch the live generator.

A recursive scan of the 26.3 `feature` assets, including inline child features, finds 56 feature types. The
26.3 registry currently implements 43 of them. The remaining types are `end_podium`, `fallen_tree`, `fossil`,
`geode`, `huge_brown_mushroom`, `huge_fungus`, `huge_red_mushroom`, `iceberg`, `large_dripstone`, `sculk_patch`,
`spike`, `tree` and `underwater_magma`. Trees, fallen trees, spikes, huge mushrooms and huge fungi belong to
the parallel tree work. The other seven remain follow-up work. In particular, the legacy iceberg algorithm
differs from 26.3 captures and is deliberately not registered as a 26.3 implementation. Unsupported types
continue to fail explicitly when compiled.
