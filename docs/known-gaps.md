# Known gaps outside the mod system

Gaps found during the mod-system work that don't limit what mods can do: vanilla parity, test plumbing and a
contract left weaker on purpose. Each entry says what's missing, why it was left and where to start.

## Clamp generation to the build range

**Status:** open. Vanilla's own dimensions never need it.

Vanilla clamps worldgen to the dimension type's build range (`NoiseSettings.clampToHeightAccessor`) when the noise
settings reach past it. Obsidian doesn't, so a dimension type override must hold the noise settings its dimension
generates with. One that doesn't, like the overworld at `min_y` 0 with vanilla's overworld noise settings from -64, is
logged at startup and left out, keeping vanilla's type (see [plugin content](plugin-content.md)).

Vanilla's own type and noise pairings never clamp, so there's no capture to check a clamped world against yet.

**Where to start:**

- Every step that reads the noise settings' range has to clamp the same way: terrain fill, base heights, aquifers,
  surface rules, carvers and structures.
- Vanilla's carvers can reach past the build range, where its chunks ignore the writes. Obsidian's chunks have to
  ignore them too.
- Capture a vanilla world generated with a mismatched type override and replay it as a parity test before dropping
  the startup check.

## Lakes, features and structures in flat dimensions

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

## Cooking, stonecutting and smithing recipes

**Status:** open, for vanilla's recipes as much as plugins'.

Obsidian reads every recipe type and keeps plugins' recipes with vanilla's (see [plugin content](plugin-content.md)),
but only crafting recipes do anything. Furnaces, smokers, blast furnaces and campfires don't cook, and the
stonecutter and smithing table have no recipe logic. The `UpdateRecipes` packet isn't sent either, so the client's
lists of what furnaces and the smithing table accept, and its stonecutter recipes, stay empty.

**Where to start:**

- Send `UpdateRecipes` from the recipe set, with vanilla's property sets and stonecutter entries, when players join
  and when the set changes.
- Tick cooking in furnace block entities from the recipe set, with vanilla's cook times, fuel burn times and
  experience.
- Add the stonecutter's and smithing table's result slots, like the crafting grid's.

## Concurrent downloads into the shared test cache

**Status:** open. Only hits a cold cache.

Tests that run an integrated server share a cache under `%TEMP%/obsidian-tests/cache`. `VanillaServerJar` downloads
Mojang's server jar to a fixed `server-<version>.jar.tmp` path there. Two test processes starting on an empty cache
write the same file at once, and one crashes. Once the cache holds the extracted structures, neither downloads.

**Where to start:** download to a unique temporary name (or take a cross-process lock on the version directory),
then extract and write the `structure.complete` marker as now.

## Scoreboard display waits only for queueing

**Status:** decided. Documented on `IPlayer.DisplayScoreboardAsync`.

`IPlayer.DisplayScoreboardAsync` returns once the scoreboard's packets are queued in the packet broadcaster, not once
they're sent. Displays, updates and removals stay in order with each other, since they're all queued under the
scoreboard's lock. A packet a plugin sends the player directly afterwards may still arrive first.

Waiting for delivery was tried and reverted: the broadcaster runs plugins' packet handlers as it sends, so a handler
that displays a scoreboard would wait on the broadcaster, which waits on it.

**Where to start, if it's ever needed:** give the broadcaster a way to flush a player's queue that packet handlers
can't reach, rather than a delivery wait.
