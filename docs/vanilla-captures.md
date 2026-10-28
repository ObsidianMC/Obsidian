# Vanilla worldgen captures

`VanillaWorldGeneration` and `VanillaStructureGeneration` replay references recorded from vanilla. The references live
in `Obsidian.Tests/Assets/vanilla_worldgen.json`. They cover noise router values, biomes, chunks after the surface,
carver and decoration steps, full chunks, the spawn, light, structure piece lists, structure blocks and a treasure map.
Each entry holds a case's inputs (seed, chunk, structure...) and the values vanilla produced for them. Each entry is one
test case.

## Re-recording

```sh
dotnet build Obsidian.AssetGenerator -t:CaptureVanillaWorldgen
```

This rewrites every captured value in `vanilla_worldgen.json` in place, using the server of the Minecraft version
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
3. starts the server again on the saved world and runs that world's cases.

Vanilla's own chunk map generates every chunk, and nothing is reimplemented. Most cases run as a single task on the
server thread, so no tick or chunk unload happens in the middle of one:

- **Router values, biomes:** `RandomState.create` and the overworld's biome source, with the case's seed.
- **Surface, carved and decorated chunks:** the chunk requested at `SURFACE`, `CARVERS` or `FEATURES`. For a decorated
  chunk, its neighbours are requested at `CARVERS` first, so only the center is decorated. A chunk that was already
  generated further fails the capture instead of producing a wrong reference.
- **Lit chunks:** the 7x7 chunks carved, the 5x5 decorated and the 3x3 lit (`LIGHT`), each in z-then-x order, then the
  center's effective sky and block light.
- **Full chunks and structure blocks:** the chunks are force-loaded (like `/forceload`) with ticks frozen, and read once
  the server ticks them. Vanilla post-processes a chunk, which applies shape updates such as fence connections, only
  when the chunk starts ticking. So a chunk that is merely `FULL` doesn't match a server world yet.
- **Structure pieces:** the start in the chunk's `STRUCTURE_STARTS`. It is described in the test's format, including
  Obsidian's class name for each structure piece type (`PIECE_CLASSES`).
- **Spawn:** the world's respawn data.
- **Treasure map:** `findNearestMapStructure`, then `MapItem.create` and `renderBiomePreviewMap`, as the shipwreck map
  loot's `exploration_map` function does.

The hash formats are defined by the tests. The harness reproduces them byte for byte, so the two must change together.

The 1.21.11 references were first recorded with an external harness that isn't in this repo. This harness reproduces
all of them exactly.
