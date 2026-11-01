# Dimension effects beyond the sky, clouds and fog


**Status:** open, deliberately deferred until a mod needs them.

The Obsidian client draws a dimension's sky, clouds and fog from its type's data, and a mod's `IDimensionEffects`
adjusts them (see [plugin content](../plugin-content.md#client)). Not covered yet:

- A few client behaviours still follow the level's name: the End's music and the dragon fight's (vanilla keys the
  latter on the End's level too), rain sounds (the overworld only) and lava's flow speed for a swimming player (the
  Nether, where vanilla reads `gameplay/fast_lava`, which isn't synced).
- Code hooks for weather, the lightmap and sunrise colours, like NeoForge's `renderSnowAndRain` and
  `adjustLightmapColors`, and cloud shapes other than vanilla's 12-block cells.
- Biomes a server sends by name only, as Obsidian's server sends vanilla's, have no attributes on the client, so
  their sky and fog colours are the dimension type's. Dimension types sent by name read the client jar's data;
  biomes could do the same.

**Where to start:** add hooks to `IDimensionEffects` as default interface methods, guarded like `Adjust`, and read
`data/minecraft/worldgen/biome/` from the jar the way `EnvironmentAssets` reads the dimension types.
