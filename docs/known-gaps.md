# Known gaps outside the mod system

Gaps found during the mod-system work that don't limit what mods can do: vanilla parity, test plumbing and a
contract left weaker on purpose. Each entry says what's missing, why it was left and where to start.

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

**Status:** open. The player's grid, crafting table and public `IServer.EvaluateCrafting` share the shaped/shapeless
matcher. Special recipes such as repairing tools and decorating shields are not evaluated yet; their recipe-specific
outputs and remainders still need implementation. Public evaluations are detached plans, not inventory reservations
or result-slot commit callbacks for custom menus.

**Where to start:** capture each supported special recipe's output and remainder behavior in vanilla 1.21.11 and
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
  below, with vanilla's chance table. Items thrown into one (item entities) aren't taken yet, and the ready tick
  is checked by scheduling and running it directly, not over a 20-tick world run.
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
- Loot `entity_properties` supports flags, type/tag, equipment and player vehicle predicates. Sheep components and
  type-specific predicates (sheep, slime and raider), and `damage_source_properties`, remain unsupported; the
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
  also resets everything. The simple server layout places displays in separate rows/parent columns rather than
  vanilla's compact tree layout; visibility-depth rules, chat announcements and full icon component JSON parsing
  remain. Display flags and telemetry are encoded, but telemetry is not collected.
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

**Status:** disabled during the 26.3 port; Phase 4 of [the porting plan](porting-26.3-plan.md) re-enables it.

26.1 replaced `HoeItem.TILLABLES` (and the axe's and shovel's tables) with the data-driven `minecraft:block_transformer`
item component: rule-based block state providers with tag and offset predicates, sounds, item damage and loot
tables. The old dump can't be made from 26.3, so hoes no longer till; `ItemUseOnBehaviors` has no counterparts and
the hoe test is skipped.

**Where to start:** read vanilla's `data/minecraft/block_transformer` files (`hoe`, `axe`, `shovel`) and run them from
the item's `block_transformer` component in `UseItemOnPacket`, then re-enable `Farmland.HoeTills`.

## Saplings that 26.3 added

**Status:** open until Phase 6 of [the porting plan](porting-26.3-plan.md).

Since 26.1 a `TreeGrower` picks from weighted feature lists. `sapling_growers.json` keeps the old shape (a feature,
a secondary one and a secondary chance), which every 1.21.11 sapling fits; a grower that doesn't fit, like the
poplar's three trees, is left out, so that sapling doesn't grow. Saplings also grow 1.21.11's configured features,
since worldgen runs on 1.21.11's data for now.

**Where to start:** give `TreeGrower` weighted lists like vanilla's, dumped as they are by `VanillaDumper`.

## Players' arm swings

**Status:** disabled during the 26.3 port; Phase 2 of [the porting plan](porting-26.3-plan.md) re-enables it.

26.3 removed the serverbound `swing` packet; the client now sends `punch` and a separate `attack` instead. Obsidian
handles neither yet, so other players don't see a player's arm swing. Mobs' own swings are still sent.

**Where to start:** handle `punch` (and `attack`, which also drives melee) and send `animate` to nearby players, as
the removed `SwingPacket` did.
