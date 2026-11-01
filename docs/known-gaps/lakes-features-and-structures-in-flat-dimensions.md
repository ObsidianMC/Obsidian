# Lakes, features and structures in flat dimensions


**Status:** open.

A `minecraft:flat` dimension only places its layers and biome. Lakes, features and structures aren't generated, so a
flat stem with `"lakes": true`, `"features": true` or any `structure_overrides` other than `[]` is refused at load,
saying which setting (see `UnsupportedFlatStemsAreRefused` in `Obsidian.Tests/LevelStemGenerators.cs`). A missing
`structure_overrides` counts too, since vanilla then places every structure set.

**Where to start:**

- Vanilla decorates flat chunks with the biome's features in the same steps as noise chunks, so reuse the noise
  generator's feature placement rather than writing a flat-only one.
- Structures need the flat generator to answer height queries for structure placement.
- Remove each refusal as its part lands, and add a parity capture of a vanilla flat world with that setting on.
