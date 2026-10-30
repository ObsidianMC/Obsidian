# Known gaps outside the mod system

Gaps found during the mod-system work that don't limit what mods can do: vanilla parity, test plumbing and a
contract left weaker on purpose. Each entry says what's missing, why it was left and where to start.

## Context number provider commands and world sources

**Status:** the shared 26.3 int/float provider evaluator and built-in definitions are available in
`Obsidian.API.Data`. Loot uses it, including arithmetic and conditional providers; captured replays check results
and subsequent random draws. `/compute` and `/data modify … compute` use it with command storage and score
callbacks. Numeric environment sampling remains unsupported in commands; see [command limits](#263-commands).
Provider holder-set tags and non-constant/non-linear enchantment-level values are not implemented.

**Where to start:** use `ContextNumberProviders.ReadInt`/`ReadFloat` (or the string `ParseInt`/`ParseFloat` overloads)
with a per-evaluation `NumberContext`. Supply numeric environment callbacks from the environment runtime and
extend the shared evaluator's remaining provider cases rather than creating a separate arithmetic evaluator.

## Low disk space notifications

**Status:** the 26.3 `low_disk_space_warning` packet is available, but the server does not monitor free disk space.
Vanilla's dedicated-server disk-space check has no corresponding Obsidian trigger. Add a rate-limited storage
monitor before sending this warning; a normal save failure alone does not establish that the disk is nearly full.

## Signed chat and block picking

**Status:** 26.3 chat and signed-command packets are decoded completely, including acknowledgement bits and checksum,
and player-chat/deletion payloads have vanilla captures. The server still delivers ordinary chat through its existing
system-message path; signature validation, acknowledgement validation and a per-connection signature cache are not
implemented. `pick_item_from_block` now reads its block position and include-data flag, but picking items into the
inventory remains unimplemented, as before the protocol port.

## Saved passenger trees and leads

**Status:** riding is a live relationship between any two entities and travel carries whole passenger trees; leads
held by entities are read, saved, shown and dropped as vanilla does around travel (see
[plugin-content.md](plugin-content.md#travel-and-portals)). Chunk saves still store each entity on its own, so a
mob-on-mob ride (other than the skeleton trap horse's rider, which finds its skeleton again by UUID) is lost on
reload; a vanilla save's nested `Passengers` list is kept as saved but its passengers aren't spawned. Using a lead on
a mob, fence knots (a knot-tied `leash` field is kept untouched) and the lead's elastic pull and snapping distance are
not modelled.

**Where to start:** save passengers nested in their root vehicle's `Passengers` list like vanilla's
`saveAsPassenger`, skipping them at the top level, and spawn and seat them when the root loads. Leads next need the
lead item's use on mobs and fences, the knot entity and `Leashable`'s distance checks.

## Item components without a saved codec

**Status:** open.

A stack saves every modeled persistent vanilla component and plugins' components exactly, but some vanilla
components are only modeled for the network (`OpaqueDataComponent`; the list is in
[plugin content](plugin-content.md#item-components)). Saving a stack leaves such a component out rather than
failing the whole chunk or player save, so it's lost across a restart: a mob-variant bucket or a `tooltip_display`
a creative player set, say.

**Where to start:** model each type's persistent codec from vanilla's `DataComponents` registration, checked
against the jar's codecs with `ComponentFixtures`, as the 63 modeled types are.

## Chunk ticket parity

**Status:** plugin tickets implemented; vanilla ticket propagation and persistence deferred.

`ILevel.AddChunkTicket` keeps a square ticking with one loaded border, rather than vanilla's propagated levels:
31 entity ticking, 32 block ticking and 33 full. The border is loaded-only; there is no separate block-ticking
ring. Natural spawning still needs players. Plugin tickets are bounded, automatically released on unload and not
saved; a mod can store coordinates with `RegisterSavedData` and reacquire handles when it starts.

Vanilla 1.21.11's `TicketType.FORCED` persists and loads/simulates without a timeout; `PORTAL` expires after 300
ticks, while `PLAYER_SPAWN` is a temporary loading ticket. `TicketStorage.TYPE` saves persistent ticket entries in
the level's `data/chunks.dat`, including forced chunks changed by `/forceload`; this is no longer the older
`ForcedChunks` long array in `chunks.dat`. Obsidian does not import those tickets or provide `/forceload` yet.

**Where to start:** capture a 1.21.11 forced-ticket save and replay it through an engine-owned saved-data codec;
add `/forceload` and the separate block-ticking border when a concrete user needs vanilla interoperability.

## Structure pieces in saved chunks

**Status:** vanilla drops the pieces of every structure Obsidian saved.

Obsidian recomputes structure starts from the seed, so `StructureStart.SaveState` writes only each piece's own state
(its bounding box and what it has placed) under `structures.starts.<id>.Children`, without vanilla's piece `id` or
its type's fields. Vanilla 26.3 opening such a world logs `Unknown structure piece id: minecraft:` for every piece
and keeps the start without them, so it loses the structure's bounds (fortress and monument spawns, for example).
Obsidian itself reads its own saves correctly. Worlds Obsidian wrote for 1.21.11 have the same shape.

**Where to start:** Phase 5's structure work (`porting-26.3-plan.md`): write vanilla's piece NBT, checked against
starts captured from vanilla, and keep Obsidian's piece state beside it.

## Attachment interoperability and saved-data migrations

**Status:** deliberately scoped to Obsidian mods.

Saved data uses vanilla's gzip envelope, but the payload's schema belongs to its plugin codec. The engine does not
run Mojang data fixers or provide a separate plugin migration registry; a plugin can include a version in its own
payload and migrate in Decode. Attachments use `obsidian:attachments` in saves and an optional Obsidian channel;
they do not read NeoForge's attachment saves or speak NeoForge's attachment payloads. NeoForge and Fabric clients
need a matching bridge mod to receive this optional channel.

**Where to start:** add compatibility only for a concrete mod and captured save/wire fixtures. Keep schema upgrades
inside that mod's codecs unless several mods need an engine migration contract.

## Comparators reading unopened loot containers

**Status:** deliberately narrowed to stored inventory contents.

Vanilla 1.21.11's `AbstractContainerMenu.getRedstoneSignalFromContainer` reads slots via `getItem`, and
`RandomizableContainerBlockEntity.getItem` calls `RandomizableContainer.unpackLootTable(null)`. Obsidian's
comparators instead read stored contents without rolling pending loot. Loot functions such as exploration maps
can synchronously load structure chunks; doing that under the level's tick lock can deadlock chunk publication.
Opening a container still rolls its loot and notifies the comparators of its changed contents.

**Where to start:** prepare loot outside the tick lock, then validate the resident container and publish the result
under the lock. Do not restore synchronous unpacking inside a comparator read.

## Recursive placement callbacks

**Status:** synchronous placement retained for vanilla parity; the deferred-hook contract is narrower.

Vanilla 1.21.11 calls `BlockState.onPlace` directly inside `LevelChunk.setBlockState`. Its collecting neighbour
updater defers nested neighbour and shape updates, not placement. Obsidian follows that order. Plugin placement
or removal callbacks that repeatedly place other blocks can recurse deeply, and can start neighbour updates before
the outer placement callback returns. Plugins must bound those chains or schedule follow-up work as block ticks.

**Where to start:** an explicit plugin-only deferred-placement API could make long chains safe while retaining
vanilla's synchronous placement ordering. Capture ordering before changing the existing hooks.

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

## Dimension effects beyond the sky, clouds and fog

**Status:** open, deliberately deferred until a mod needs them.

The Obsidian client draws a dimension's sky, clouds and fog from its type's data, and a mod's `IDimensionEffects`
adjusts them (see [plugin content](plugin-content.md#client)). Not covered yet:

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

## Special crafting recipes

**Status:** open. The player's grid, crafting table and public `IServer.EvaluateCrafting` share one matcher, for
shaped and shapeless recipes and 26.1's configurable `crafting_dye`, `crafting_imbue` and `crafting_transmute`
(`SpecialCrafting`). The older special recipes (`crafting_special_*` and `crafting_decorated_pot`), such as repairing
tools and decorating shields, are not evaluated yet; their recipe-specific outputs and remainders still need
implementation. A dye, imbue or transmute result whose component patch sets a component Obsidian can't read from a
saved stack (see [item components without a saved codec](#item-components-without-a-saved-codec)) fails to load
rather than crafting without it. Public evaluations are detached plans, not inventory reservations or result-slot commit callbacks for
custom menus.

**Where to start:** capture each supported special recipe's output and remainder behavior in vanilla 26.3 and
extend the shared matcher and consumption path together. Add a custom-menu commit API when a concrete menu needs it.

## Workstation details

**Status:** mostly done. The anvil, smithing table, stonecutter, loom, grindstone and cartography table work like
vanilla's menus (`MainEventHandler.Workstations`), with vanilla's and plugins' stonecutting and smithing recipes.

- The smithing table finds a trim's material from a fixed item list rather than the addition's
  `provides_trim_material` component, so a plugin item can't supply a trim material.
- The loom's patterns come from `BannerPatternsRegistry`, which only has vanilla's patterns and pattern items.
- The stonecutter's, loom's and cartography table's take sounds go only to the player using them, and a cartography table's map is made new
  as it's taken, even when the take then fails.
- The beacon, lectern and crafter work too (`RedstoneBlockEntities`, `MainEventHandler.RedstoneMenus`). A beacon
  measures its pyramid and beam all at once every 80 game ticks rather than a layer a tick, and doesn't fire
  `construct_beacon`. A crafter crafts only shaped and shapeless recipes, like the crafting table.

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
[plugin content](plugin-content.md#block-hooks)). Of vanilla's blocks, liquids flow, falling blocks fall and the basic
redstone components work (see [Redstone](#redstone)). The existing shape rules (`ShapeUpdater`) now also run for
changes players, plugins and explosions make: fences, walls, panes and stairs connect; supported attachments and
plants pop off with tool-less loot and break effects; doors, beds and double plants react when their partner goes.
Removal is queued on the serial level tick so loot never runs under the tick lock. Shape destruction respects
`SuppressDrops` and `block_drops`. Water washout drops loot without the break effect; lava only fizzes.
Rails check their base and ascending end support and drop without a break effect. Vanilla's `neighborChanged`
reactions other than redstone components' (rails, doors and trapdoors opening when powered, TNT) aren't modelled,
and scheduled ticks of vanilla blocks other than falling blocks and redstone components (an observer's, from a
vanilla save) do nothing when they come due. Of vanilla's own blocks, only pressure plates and wooden buttons react
to entities inside them, and none to entities standing on them; and placing a block other than a redstone component
doesn't check that it can survive there, nor any block that no entity is in the way.

Saved `block_ticks` and `fluid_ticks` lists are written in the order the ticks would run, where vanilla writes its
priority queue's internal heap order. Both reload the list in file order, so after a save and reload Obsidian keeps
running ticks due the same game tick with the same priority in their original order, while vanilla runs them in a
different one. Obsidian deliberately doesn't emulate Java's heap layout for this.

**Where to start:**

- Vanilla blocks' `neighborChanged` goes in `LevelBlockUpdates.NeighborChanged`, and their ticks in
  `LevelBlockUpdates.RunScheduledTick`, next to falling blocks'.
- Placement checks belong in `UseItemOnPacket`'s placement, before the place event: vanilla's `canSurvive` and
  `isUnobstructed`.

## Redstone

**Status:** open. Dust, torches, repeaters, comparators, levers, buttons, pressure plates, the block of redstone and
the redstone lamp work (see [plugin content](plugin-content.md#redstone)); the rest of vanilla's redstone doesn't.

- **Consumers.** Doors, trapdoors and fence gates, pistons, note blocks, powered, activator and detector rails, TNT,
  bells and copper bulbs don't react to power. Hoppers lock, droppers and dispensers fire, and crafters craft (see
  [item access](#item-access-beyond-containers)).
- **Sources.** Observers, daylight detectors, target blocks, trapped chests, tripwire hooks, detector rails,
  lightning rods and sculk sensors give no power (a lectern pulses as its page turns), though dust connects to them as to any signal source.
- **Analog outputs.** Comparators read 0 from jukeboxes, chiseled bookshelves, decorated pots,
  shelves, command blocks, sculk sensors, detector rails, creaking hearts, copper bulbs, copper golem statues and
  copper chests.
- **Timing.** Comparators hear of a container's items changing at the end of the level's block entity tick (through
  item access) or on its next one (players' clicks), and only near players, where vanilla's `setChanged` tells them
  at once; and entities press plates and arrows press buttons after they tick, where vanilla's do as they move.
  Cats sitting on a chest don't stop comparators reading it.
- **Game events.** Levers, buttons and pressure plates don't emit vanilla's `block_activate` and `block_deactivate`
  vibrations.
- **Experiments.** The `redstone_experiments` feature's wire evaluator and update orientations aren't modelled.

**Where to start:** each consumer is another `RedstoneComponent` in `Obsidian/WorldData/Redstone`, listed in
`VanillaRedstone`, reacting in `NeighborChanged` through `LevelBlockUpdates`' signal methods, like the lamp; sources
give their power from `GetSignal` and `GetDirectSignal`, like the lever. Analog outputs go in `AnalogOutputs`.

## Item access beyond containers

**Status:** open. Item access (see [plugin content](plugin-content.md#item-access)) covers vanilla's containers,
hoppers, droppers and dispensers; the rest of vanilla's item movers aren't modelled.

- **Dispensers** drop every item, like vanilla's default behaviour. Vanilla's item behaviours (shooting arrows and
  other projectiles, pouring and filling buckets, bone meal, placing shulker boxes and other blocks, equipping
  armour, igniting TNT, shearing, ...) aren't modelled, and a dispenser with nothing to fire doesn't emit vanilla's
  `block_activate` game event.
- **Composters** take items put in by hand and by a hopper above, and give bone meal out by hand and to a hopper
  below, adding the layers each item's `minecraft:compostable` gives. Items thrown into one (item entities) aren't
  taken yet, and the ready tick is checked by scheduling and running it directly, not over a 20-tick world run.
- **Minecarts with chests or hoppers** are neither containers hoppers reach (vanilla's `getEntityContainer`) nor
  hoppers themselves.
- **Hopper timing.** A hopper takes in item entities on its own tick, not also as they move into it (vanilla's
  `entityInside`), and takes them in entity id order rather than vanilla's entity section order. Hoppers tick in
  their chunk's block entity order, not vanilla's level-wide list; the cooldown rules make chains independent of
  it, but contraptions that race separate hoppers may differ. The timings are reasoned from the 1.21.11 jar's
  `HopperBlockEntity`, not checked against a vanilla capture.
- **Inside block hooks**, which hold the level's tick lock, a container a player's click holds at that moment, or
  one still waiting for its loot table, takes and gives nothing for that call, where vanilla's single thread never
  meets either case.
- **Fluids** have no access API yet; one would follow `IItemAccess`'s shape (amounts instead of stacks, the same
  simulate flag and sides) when a mod needs it.

**Where to start:** dispenser behaviours go in `DispenserComponent.Fire`, in
`Obsidian/WorldData/Redstone/ItemMovers.cs`, keyed by item like vanilla's `DispenserBlock.DISPENSER_REGISTRY`; a
composter's access is `ComposterItemAccess` (`Obsidian/WorldData/AbstractLevel.Composters.cs`), which
`AbstractLevel.GetItemAccess` returns before it looks for a block entity.

## Sleeping and spawn points

**Status:** mostly done. Players sleep in beds, skip the night by `players_sleeping_percentage`, and respawn at
their bed or charged respawn anchor, saved in vanilla's `respawn` field (see `PlayerBase.Sleeping`).

- Unlike vanilla's, a respawn anchor next to water explodes like any other.
- Waking stands the player up on the first free spot around the bed, not by vanilla's
  `BedBlock.findStandUpPosition` order.
- `/spawnpoint` doesn't set a forced respawn point yet, and villagers in a bed don't keep players out of it.

## Mods' menus in the inventory screen

**Status:** open.

The Obsidian client's inventory screen has no button or tab for mods' menus, like Curios' inventory button, to open
the menu of a mod's extra player slots (see [plugin content](plugin-content.md#player-extensions)); a mod opens its
menu with a key binding's payload or its own screen.

**Where to start:** add the inventory button only once a mod needs one, as an `IClientRegistry` registration the
inventory screen shows.

## Scoreboard display waits only for queueing

**Status:** decided. Documented on `IPlayer.DisplayScoreboardAsync`.

`IPlayer.DisplayScoreboardAsync` returns once the scoreboard's packets are queued in the packet broadcaster, not once
they're sent. Displays, updates and removals stay in order with each other, since they're all queued under the
scoreboard's lock. A packet a plugin sends the player directly afterwards may still arrive first.

Waiting for delivery was tried and reverted: plugins' packet handlers run in the player's outgoing pipeline as it
sends, so a handler that displays a scoreboard would wait on the pipeline it runs in.

**Where to start, if it's ever needed:** a delivery wait that packet handlers can't reach, such as one that refuses
to run inside the pipeline.

## Item components the server doesn't apply yet

**Status:** open. Found while honouring plugin items' default components; vanilla items have the same gaps.

- Breaking time isn't checked: a player's client times breaking from the held tool's mining speed and the server
  accepts the finished break. The tool component still decides drops and wear.
- Mobs' equipment modifiers don't go into their attributes: mobs add their armour items' armour, toughness and
  knockback resistance where they're hit, and their held item's attack damage where they attack. Players apply
  every modifier of what they hold and wear. Enchantments' attribute effects aren't applied to anyone.
- Equipping by use ignores `equippable`'s `swappable` and plays no equip sound, and a `consumable` without `food`
  isn't consumed.

**Where to start:** for mobs' attributes, `Player.UpdateEquipmentAttributes`'s per-slot modifier tracking, run on a
mob's equipment.

## Plugin entities

**Status:** plugin entity foundations complete; remaining vanilla mount and loot parity is deferred.

- The server reads `MoveVehicle` only for plugin mobs. Vanilla's mounts (horses, pigs, striders, camels, happy
  ghasts, nautiluses) still move on the server from the rider's `PlayerInput`, while the Obsidian client predicts
  boats, horses, pigs and striders on its own, and neither corrects the other. The Obsidian client's prediction of a
  mod's mount leaves out fluid currents, which the server's checks absorb.
- Vanilla table loot is enabled for zombies, husks, drowned, zombie villagers, skeletons, strays, bogged, parched,
  wither skeletons, spiders, cave spiders, creepers, endermen, blazes, breezes, witches, zombified piglins, cows,
  mooshrooms, pigs, chickens, rabbits, hoglins and zoglins. Every other type keeps its previous death handler.
  The remaining hard-coded item drops are sheep, fish, dolphins, guardians and elder guardians, squid and glow
  squid, ghasts, slimes and magma cubes, turtles, polar bears, parrots, phantoms, shulkers, snow and iron golems,
  pillagers, vindicators, evokers, ravagers, piglins and piglin brutes, striders, wardens and withers.
  Horses and their relatives, happy ghasts, nautiluses, allays and copper golems keep their equipment or special
  drops; the dragon keeps its separate death sequence. `Mob.Loot.cs` holds the explicit migrated type list.
- Loot uses 26.3 tables and namespaced entity predicate maps. `entity_properties` supports flags, entity type/tag,
  equipment, vehicle and fishing-hook open-water predicates. Sheep components and type-specific predicates
  (sheep, cube mob and raider), and `damage_source_properties`, remain unsupported; the
  generator excludes sheep, slime, magma cube, pillager, ghast and turtle root tables rather than relaxing their
  conditions. Damage sources still name the vanilla damage type closest to Obsidian's damage kinds.
- `IEntityBehavior` has no hook for a player getting on or off. A behaviour that reacts to mounting takes the
  interaction itself and seats the player with `IPlayer.StartRiding`, as ObsidianMod's moth does to count its
  rides; dismounting, by sneaking or otherwise, can't be observed.

**Where to start:** `ClientVehicleControl` checks moves for any mob; giving vanilla's mounts the same client control
needs their server steering to match the client's `VehicleControl`, as `MountSteering` does for plugin mobs.

## Block entity data on items

**Status:** partly done. A plugin's block entity keeps its data on its block's item (`BlockEntity.CollectComponents`
and `ApplyComponents`, see [plugin content](plugin-content.md#block-entities)).

- Creative pick-block (`PickItemFromBlock`) isn't handled, so no block entity's data reaches a picked item, and a
  placed item's `block_entity_data` isn't loaded into its block entity, which vanilla does before
  `applyImplicitComponents` (an operator's or a creative player's raw block entity data).

Plugin component values default to hashes of their saved NBT, which matches vanilla's `HashOps` for codecs writing
NBT-shaped values. Codecs whose types NBT loses (such as booleans stored as bytes) must supply
`ComponentNetworkCodec<TValue>.Hash` to match NeoForge or Fabric predictions. Without it, hashes can differ. The
server currently ignores client click predictions and sends authoritative contents after a click; hash mismatches
do not change the click it applies.

**Where to start:** pick-block in `PlayClientHandler`, collecting through the same path as breaks
(`BlockEntityComponents.CaptureAsync` without retiring a container).

## Progress parity

The core progress API, vanilla save envelopes and packet fields are implemented. These gaps keep phase 5's
Progress item from being marked done:

- Advancement visibility is currently reset-plus-all, including recipe and hidden definitions. Content reload
  also resets everything. Displays use the server's compact tree layout, replayed against vanilla 26.3 captures;
  visibility-depth rules, chat announcements and full icon component JSON parsing remain. Display flags and telemetry
  are encoded, but telemetry is not collected.
- Supported triggers are listed in [Progress](plugin-content.md#progress). `bred_animals` is deferred because
  breeding currently keeps no responsible-player identity. Other trigger ids never fire.
- Predicate support is deliberately strict. Unsupported condition fields never match, including component
  predicates other than active enchantments, entity flags/equipment/effects/subpredicates, killing-blow/damage
  predicates, biome/structure/light/fluid/smokey location predicates, `recipe_crafted` ingredients, location
  offsets, and arbitrary loot predicate terms. Add these to `ProgressConditions` with captured references before
  enabling the affected vanilla advancements.
- `inventory_changed` observes inventory versions on a tick, so transient changes between ticks are not captured.
  `location` runs every 20 ticks. Placement, plugin item-on-block uses and portal igniters fire
  `item_used_on_block`; successful bucket/entity use, music-block, campfire and queued bone-meal operations still
  need their own confirmation hooks. There is no broad attempt-based fallback that could grant failed actions.
- Recipe rewards explicitly add existing displays, persist known recipe ids in `recipeBook.recipes`, and fire
  `recipe_unlocked` on first reward unlock. The existing recipe book still exposes all crafting recipes on join;
  selective unlocking and vanilla recipe-book settings/highlights are a separate change.
- Function reward identifiers are read but cannot execute: the server has no Minecraft function system. Loot
  rewards use the existing loot evaluator and inherit its unsupported-condition/table-loading limitations.
- Automatic statistics cover play/world time, death/rest/sneak timers, deaths, jumps, common movement distances,
  combat damage and kills, mining, crafting, consumables, block placement, plugin uses, bows/crossbows/tridents,
  thrown eyes, item breakage, pickups and drops. Remaining vanilla custom interactions, vehicle distances,
  scoreboard stat criteria, and item uses outside those confirmed hooks need additional event integration.
  The requested swim/distance stats follow the server's existing movement flags and terrain checks; complete
  physics/swimming validation is outside progress. Statistics are sent on request, not periodically unsolicited.
- Custom stat ids are synced and remain reserved across unload. The Obsidian client's registry API still needs
  matching custom-stat registration before a client-side plugin can declare these through the same method.
  Server-only players (`ServerPlayer`) and alternative `IPlayer` implementations retain the default unsupported
  progress accessors; the persisted implementation currently belongs to connected `Player` instances.
- `/advancement` currently accepts an online player and `only`, with an optional criterion. Selector targets and
  `everything`, `from`, `until`, `through` modes are deferred. Older save versions are retained/read without
  running Mojang data fixers; renamed historical stat/advancement ids are not migrated.

Continue in `Obsidian/Progress`, `Player.Progress.cs`, and the three registries for advancements, triggers and
custom statistics. Existing gameplay files only contain small calls into that runtime. The three tests in
`Obsidian.Tests/Progress.cs` replay captured vanilla 1.21.11 packet/save fixtures and exercise reward idempotence
and plugin unload/reload.

## Tilling and other block transformers

**Status:** implemented for vanilla 26.3's transformers.

`UseItemOnPacket` runs the stack's `block_transformer` component after block and plugin interactions. Hoes till,
axes strip, scrape and remove wax, and shovels make paths. The generated registry supplies predicates, states,
sounds, particles, durability and rooted-dirt loot. Component overrides and removals work on other items too.
`DataSystemsFixtures.java` captures vanilla's provider results; `DataSystems` replays them and exercises packet use.

Gameplay tag uses were checked against 26.3's tags. Mushroom survival now uses
`overrides_mushroom_light_requirement`; the removed `mushroom_grow_block` tag is only retained for Phase 5's legacy
world generation.

## World clocks, timelines and decorative registries

**Status:** Phase 4 data support implemented.

Game time and the overworld and End clocks are independent. Dimensions share the world's clocks; saves and full
time syncs retain each clock's ticks, fractional tick, rate and paused state. Sky light, monster burning, early-game
patrol tracks and sleep markers come from the generated timeline data. `/time of <clock>` selects a clock for
set, add, pause, resume, rate and query operations. Vanilla's command failure semantics and timeline-specific
queries are implemented, including timeline ticks/repetitions and failures for unchanged clock state.

Trim materials retain 26.3's `palette_id` and text description. Pottery patterns and their item components are read
from the generated registries, including stack overrides. Their values, clocks and timeline sampling are compared
with the real vanilla capture in `DataSystemsFixtures.java`.

## 26.3 commands

**Status:** Phase 6's command additions and server defaults are implemented, with the limits below.

`/swing`, clock-based `/time`, `/compute`, `/posteffect`, `/unpublish`, slot sources in `/item` and
`/execute if|unless items|slots`, and inline `/place feature` have command modules. `/data modify … compute`
evaluates integer and float providers into NBT; storage uses vanilla's per-namespace `command_storage.dat` envelope.
Commands expose success separately from their integer result, so a successful zero and an arithmetic failure remain
distinct through `/execute run`. Class-level command permissions apply to commands outside groups too.

The generated `commands-26.3.json` supplies vanilla's client grammar, argument bytes, suggestion identifiers,
restriction flags and redirects. It describes the complete vanilla trees, including older branches the existing
command implementations still lack. Server completion handles the new literals, clock markers, timelines, provider
identifiers and slot names. `CommandFixtures.java` captures the grammar and execution references;
`VanillaCommands26` replays them, including nested container contents, bundles and charged projectiles growing
within their limits, and distribution across targets.

- Number providers share the data-system evaluator. Score and storage inputs work; numeric environment-attribute
  sampling fails explicitly until the environment runtime supplies a general numeric sampler. Predicates inherit
  the shared loot evaluator's limitations.
- `/data modify … compute` supports compound names and individual list indices, plus numeric set/insert/prepend/
  append. Wildcard and compound-match paths are unsupported. Other modify sources and general compound merging
  remain outside the existing `/data` implementation.
- Slot sources support literal ranges, inline groups/ranges, limits, item/count filters and contents. Players'
  inventories/equipment, mobs' equipment and block containers are exposed. Vehicle inventories, registered slot
  sources/modifiers, inline item component patches and component-rich item predicates remain unsupported.
- Inline features use the 26.3 feature registry and the live feature view. Placement requires its surrounding
  chunks to be loaded; unsupported feature types fail explicitly. Feature entity spawning in a live level is not
  implemented. The other `/place` modes remain unsupported.
- `/execute` retains its earlier supported context operations and conditions; storage callbacks (`store`), functions
  and the rest of vanilla's older execution branches are still absent.

Whitelist defaults to on for fresh installs, whose generated `server.json` sets it explicitly; an existing
`server.json` without a `whitelist` entry keeps it off and gets a startup notice. An empty optional IP whitelist no
longer rejects every connection before player login;
integrated worlds use their existing owner/LAN admission policy. Chat and commands have independent ten-second
spam budgets, with vanilla's 20-tick charge, one-tick recovery, boundary and disabled-limit behavior. Operators and
the integrated owner are exempt from disconnection.

## Villager trades

**Status:** Phase 4 trades implemented.

Offers come from the 26.3 trade sets in vanilla's draw order (see `TradeFixtures.java`). What remains:

- Random sequences are kept per level in memory, shared with loot, and not saved. Vanilla keeps one set per server,
  across dimensions, in its saved data, so offers drawn after a restart differ from vanilla's.
- Trade data can only give or want the `minecraft:potion_contents` component, the only one vanilla's trades use.
- Plugins can't add trades or trade sets yet.
- Gossip and Hero of the Village discounts (an offer's special price) aren't applied.

## 26.3 blocks

**Status:** Phase 6 step 1's sulfur slice is implemented. The remaining block groups need their own captures and
implementation PRs:

- Golden dandelion interactions and persisted age locking, including stopping aging.
- Note block trumpet instrument selection and playback.
- Poplar wood behavior, wool and concrete stairs/slabs, colored leaves and the straw bed's sleep rules. Some of
  these already inherit generic block-family behavior; that is not a completed parity audit. Poplar sapling growth
  is tracked separately below.

Sulfur, chiseled/polished sulfur and sulfur bricks use the generated properties, sounds, shapes and loot. Their
stairs, slabs and walls share the existing placement and waterlogging APIs. Potent sulfur validates its water and
heat source, saves its countdown, times periodic eruptions with positional random, applies gas nausea and launches
entities. Block events start the vanilla client's geyser particles and sound loop; ambient bubbles and gas particles
are client behavior. Sulfur spikes support placement, shaping, delayed support loss, random growth, falling,
shattering and fast-trident breakage. `VanillaSulfurBlocks` replays `sulfur_scenarios.json` and `sulfur_fixtures.json`;
the latter is recorded by `SulfurFixtures.java` through `CaptureFixtures=sulfur`.

Remaining sulfur limits:

- Falling spikes use the shared simplified falling-block flight and collision implementation. Captures cover
  support-loss timing, shattering, raw impact damage and vertical sand contacts with full/partial floors and a
  moving piston. General horizontal flight and per-tick spike trajectories remain unaudited. Falling
  entities do not yet persist the spike's damage multiplier. Helmet-specific damage reduction and durability loss
  for `damages_helmet` damage sources are not implemented in the shared combat path.
- Tridents respect the projectile-breaking and mob-griefing game rules and reject adventure/spectator owners.
  Adventure-mode projectile `can_break` predicates are not evaluated yet.
- Gas uses the existing entity eye-height implementations; remaining species/pose eye-height differences belong
  to the entity behavior work.

**Where to start:** add cases to the two sulfur captures before extending these behaviors. The remaining block
groups need separate scenario references. Sulfur cubes and cushions remain Phase 6 step 2 work.

## Entity behavior beyond 26.3 metadata

**Status:** metadata layouts match the 26.3 fixture; the following behavior remains open.

- Sulfur cubes expose the ageable flags, size, fuse and bucket flag, but have no archetype selection, AI, bounce
  physics, fuse/explosion behavior or bucket interactions. Cushions expose their color but have no seating,
  movement or item placement/drop behavior. Their type-specific state is not persisted yet.
- Age locking is sent to clients, but golden dandelion interactions, stopping aging and saving the lock remain
  unimplemented. Cat, cow, pig and chicken sound variants default to the classic registry entry; choosing,
  inheriting, saving and playing those variants remains unimplemented.
- Block and item displays write vanilla's default transformation and empty contents, but have no editing or
  type-specific persistence. Interaction entities expose width, height and response metadata, but do not apply
  those dimensions on the server or record attacks/uses. Ominous item spawners expose their item but do not time
  or release it. Markers and player wind charges use the nonliving entity layout; marker data and wind-charge
  projectile/impact behavior remain unimplemented.
- Villagers and wandering traders send an inactive unhappy counter; villagers send an unfinalized data flag.
  Those animation/finalization transitions are not modeled yet.

Poplar boats and chest boats use the existing boat implementation, including its item mapping and inventory.

**Where to start:** `Obsidian/Entities` and its factory; `EntityMetadataParity` checks every entity type and all 44
serializer ids, requires complete layouts for ageable mobs and the metadata implementations above, and checks
representative holder and primitive payloads against the captured serializer bytes.

## Saplings that 26.3 added

**Status:** open until Phase 6 of [the porting plan](porting-26.3-plan.md).

Since 26.1 a `TreeGrower` picks from weighted feature lists. `sapling_growers.json` keeps the old shape (a feature,
a secondary one and a secondary chance), which every 1.21.11 sapling fits; a grower that doesn't fit, like the
poplar's three trees, is left out, so that sapling doesn't grow. Saplings also grow 1.21.11's configured features,
since worldgen runs on 1.21.11's data for now.

**Where to start:** give `TreeGrower` weighted lists like vanilla's, dumped as they are by `VanillaDumper`.

## Movement and swing details

**Status:** open.

- **Swings from uses.** Since 26.3 the server decides when a use swings an arm: a use that succeeds swings it for
  the players around (`InteractionResult.SUCCESS`) or for everyone (`SUCCESS_SERVER`). Obsidian doesn't track its
  uses' results, so only attack swings (`punch`) and mobs' attacks are shown to other players.
- **Stepped movement.** Vanilla sends living entities' per-tick positions between updates as stepped `VecDelta`s and
  `PositionPath`s. Obsidian sends one linear move per update, so a mob's path between updates is a straight line.
- **Moves during a teleport.** Vanilla ignores a player's moves until the client accepts the teleport it was sent.
  Obsidian applies them as they come.

**Where to start:** have `ItemUseOnBehaviors` and the entity interactions in `MainEventHandler` report vanilla's
result, and swing as `Living.SwingForAttack` does; record a per-tick step history on `Mob` for `EntityMoveDelta`'s
stepped forms.

## Bubble columns started by water

**Status:** disabled during the 26.3 port; Phase 6 of [the porting plan](porting-26.3-plan.md) re-enables it.

Since 26.1 soul sand and magma no longer start bubble columns themselves. The water above them does: `LiquidBlock`'s
`onPlace`, `neighborChanged` and `updateShape` (from below) schedule a 20-tick check when the block below is in
`#enables_bubble_column_push_up` or `#enables_bubble_column_drag_down`, and its `tick` updates the column. Obsidian's
soul sand and magma behaviors no longer bind to an owner, so new columns don't form; existing columns still tick. The
two bubble column tests that start a column and send it to the client are skipped; a Phase 6 vanilla capture will
cover the rest of the behavior.

**Where to start:** give water the same hooks (Obsidian's fluid ticks handle water today, so keep its spreading path)
and replace `SoulSandBlock` and `MagmaBlock` in `BlockOwnerBehaviors` with them, then re-enable the skipped tests in
`BubbleColumns`.

## Item component numbers

**Status:** mostly done. Furnaces, brewing stands and composters read 26.3's `minecraft:cooking_fuel`,
`minecraft:brewing_fuel` and `minecraft:compostable` components (`WorldData/ItemFuels.cs`), and brewing stands brew
by the `minecraft:brewing` recipes.

- The components' numbers name context number providers, which `WorldData/ComponentNumbers.cs` works out with only
  the provider and predicate types vanilla's fuel and compost providers use. It stands in for Phase 4's context
  number providers and should be replaced by them.
- A brewing fuel's numbers are worked out for a default brewing stand rather than the stand's own block state, and a
  fuel's crafting remainder (none of vanilla's brewing fuels has one) isn't given back.
- Brewing outputs can set only `minecraft:potion_contents`, and potion tags in brewing inputs aren't read.
- `minecraft:villager_food` and `minecraft:mob_visibility` aren't read: villagers don't pick up or share food, and
  mobs don't see players wearing heads any less yet.

**Where to start:** once context number providers land, resolve `ComponentIntValue` and `ComponentFloatValue` with
them and delete `ComponentNumbers`.
