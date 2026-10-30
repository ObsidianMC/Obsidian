# Vanilla worldgen captures

The worldgen parity tests replay references recorded from vanilla. Each entry holds a case's inputs (seed, position,
noise...) and the values vanilla produced for them, and each entry is one test case. There are two fixtures while
Phase 5 of `docs/porting-26.3-plan.md` builds the 26.3 generator beside the legacy one:

- `Obsidian.Tests/Assets/vanilla_worldgen_26.3.json` holds 26.3 references, which `VanillaDensityFunctions` replays:
  noise samples (`noises`), the value of every noise router and aquifer function of each dimension's noise settings
  at block positions (`router`), and inline density functions at block positions (`functions`), for what no router
  reaches. 26.3 evaluates noise and density in single precision, so the values are floats and must match bit for bit;
  `functions` records them as `Float.floatToIntBits` in hex too (`bits`), since JSON numbers hold neither NaN nor
  infinities. `terrain` holds chunks at the merged terrain status, and `materials` holds small, single-rule boundary
  cases. `VanillaTerrainGeneration` and `VanillaMaterialRules` replay those sections; see below.
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
  properties, terracotta bands and the ore rule's acceptance/filler/raw-ore paths.

## Terrain references

All terrain cases use seed `12345`. The nine overworld locations cover ocean and flooded caves `(0, 0)`, iron veins
and underground fluids `(-10, 0)`, meadow/deep dark `(30, 40)`, frozen ocean `(100, -100)`, stony peaks `(-44, -16)`,
snowy slopes `(-24, 48)`, eroded badlands `(-304, -280)`, desert/river `(-140, 28)` and sulfur caves `(56, 68)`.
Nether cases cover wastes `(0, 0)`, warped forest `(20, -30)`, basalt deltas `(-16, 16)` and soul-sand valley
`(-24, 4)`. End cases cover the central island `(0, 0)` and outer islands `(80, -60)`.

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
lookups explicitly: Phase 5's biome step will supply them, while these replays supply vanilla's captured inputs.
`DensityCompiler` accepts a beardifier function; terrain defaults it to its neutral contextual value until structures
are implemented. None of this switches the live generator or changes its 1.21.11 data.

Chunk replays have `Category=Worldgen` and stay out of CI. The 64 small material-rule replays run in CI alongside the
density tests. Biome selection, features, structures, the remaining Nether/End pipeline and performance work remain
for the later Phase 5 steps. The noise-input point-sampling allocations noted in #597 are unchanged.

The 1.21.11 probe also captured lit/full chunks, structure pieces and blocks, the spawn and a treasure map (in git
history before Phase 5). The feature and structure steps adapt those captures to 26.3 as they need them.
