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

## Plant growth beyond saplings

**Status:** open.

Random ticks grow saplings and the mangrove propagule, and bone meal grows saplings, azaleas, crops, grass blocks and
short grass and ferns (see [plugin content](plugin-content.md#saplings)). Crops don't grow on random ticks, and bone
meal does nothing to vanilla's other plants (stems, berries, mushrooms, moss, nylium, cocoa, water plants and the
like).

Random ticks sample the same sections and draw the same positions as vanilla's, but only saplings and the propagule
do anything with them: leaves don't decay, ice and snow don't melt, copper doesn't weather, and so on. Lava's random
tick draws its chances in vanilla's order but sets nothing on fire, since Obsidian has no fire spread or flammability.
Vanilla's chunk ticks also draw for rain and snow (`tickPrecipitation`, one time in 48 per random tick), which
Obsidian neither draws nor does, so the level random and the position sequence part from vanilla's there.

**Where to start:** each plant is another case of `Growth` (its random tick, bone meal target, chance and growth),
placing features through `LiveFeatureLevel`; crops' random ticks need vanilla's `CropBlock.getGrowthSpeed`. Other
blocks' random ticks go next to the plants' in `AbstractLevel.TickChunkBlocks`, and lava's fire in
`AbstractLevel.RandomTickLava` once blocks know vanilla's `ignitedByLava`.

## Vanilla blocks' reactions to block updates

**Status:** open. The mechanism is done; vanilla's blocks get their rules over time.

Block changes run vanilla's neighbour and shape updates, in vanilla's order, and plugin blocks react to them (see
[plugin content](plugin-content.md#block-hooks)). Of vanilla's blocks, liquids flow and falling blocks fall, but the
others don't react to changes players, plugins and explosions make: fences, walls, panes and stairs don't connect,
torches, ladders, signs, plants and carpets don't pop off without support, and a door's other half stays. Those
shape rules (`ShapeUpdater`) only run in generation and when water or lava changes blocks, as before, since a block
they destroy would vanish without its drops: `FluidLevel.DestroyBlock` doesn't drop anything yet. Vanilla's
`neighborChanged` reactions (redstone, rails, doors and trapdoors opening when powered, TNT) aren't modelled, and
scheduled ticks of vanilla blocks other than falling blocks (a repeater's or an observer's, from a vanilla save) do
nothing when they come due. Vanilla's own blocks don't react to entities inside them or standing on them through
the hooks either, and placing a block doesn't check that it can survive there or that no entity is in the way.

Saved `block_ticks` and `fluid_ticks` lists are written in the order the ticks would run, where vanilla writes its
priority queue's internal heap order. Both reload the list in file order, so after a save and reload Obsidian keeps
running ticks due the same game tick with the same priority in their original order, while vanilla runs them in a
different one. Obsidian deliberately doesn't emulate Java's heap layout for this.

**Where to start:**

- Make `FluidLevel.DestroyBlock` drop the block's loot (vanilla's `Level.destroyBlock`, unless `SuppressDrops`),
  then run every chain with `VanillaShapes.All`, which makes `FluidLevel.OnBlockChanged` apply `ShapeUpdater`'s
  rules too. Check beds and doors, whose breaking code removes the other half itself.
- Vanilla blocks' `neighborChanged` goes in `LevelBlockUpdates.NeighborChanged`, and their ticks in
  `LevelBlockUpdates.RunScheduledTick`, next to falling blocks'.
- Placement checks belong in `UseItemOnPacket`'s placement, before the place event: vanilla's `canSurvive` and
  `isUnobstructed`.

## Sleeping and spawn points

**Status:** open.

Beds and respawn anchors explode where their environment attributes say they don't work (see
[plugin content](plugin-content.md#whats-checked)), but where they work, players can't sleep in a bed or set their
spawn point with either: using one does nothing more, and players respawn at the world spawn. Unlike vanilla's, a
respawn anchor next to water explodes like any other.

**Where to start:** sleeping (vanilla's `Player.startSleepInBed` and the night skip) and players' respawn points,
which `bed_rule`'s `can_sleep` and `can_set_spawn` and `respawn_anchor_works` then decide (see `LevelEnvironment`).

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
