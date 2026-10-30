# Vanilla worldgen captures

The worldgen parity tests replay references recorded from vanilla. Each entry holds a case's inputs (seed, position,
noise...) and the values vanilla produced for them, and each entry is one test case. There are two fixtures while
Phase 5 of `docs/porting-26.3-plan.md` builds the 26.3 generator beside the legacy one:

- `Obsidian.Tests/Assets/vanilla_worldgen_26.3.json` holds 26.3 references, which `VanillaDensityFunctions` replays:
  noise samples (`noises`), the value of every noise router and aquifer function of each dimension's noise settings
  at block positions (`router`), and inline density functions at block positions (`functions`), for what no router
  reaches. 26.3 evaluates noise and density in single precision, so the values are floats and must match bit for bit;
  `functions` records them as `Float.floatToIntBits` in hex too (`bits`), since JSON numbers hold neither NaN nor
  infinities. Each step of Phase 5 adds its sections here.
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

The 1.21.11 probe also captured chunks at each status, lit and full chunks, structure pieces and blocks, the spawn and a
treasure map (in git history before Phase 5). The terrain, feature and structure steps of Phase 5 adapt those captures
to 26.3's chunk pipeline as they need them; the hash formats are defined by the tests, and the harness reproduces them
byte for byte, so the two must change together.
