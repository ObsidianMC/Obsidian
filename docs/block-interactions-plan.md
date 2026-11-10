# Block interactions plan (issue #538)

A plan for finishing [issue #538](https://github.com/ObsidianMC/Obsidian/issues/538), the block interaction
tracking checklist. The performance work that goes with it is in
[block interaction performance](block-interactions-performance.md).

Written 2026-11-09 against `26.3.x` at `cd79d4dd6`, then re-checked at `066dd1f01` for the placement rewrite
(#695) and instant mining (#694), which landed in between. Line numbers are as of `066dd1f01`.

## Where the checklist really stands

The issue has 210 lines and eight of them are ticked. The code is far ahead of that. Every line was traced from its
trigger (packet, tick or hook) to the state change, reading the code and its tests. Nothing was run.

| Section | Lines | Works | Partial | Missing |
|---|---|---|---|---|
| 1. Doors, switches, mechanical | 16 | 14 | 0 | 2 |
| 2. Containers | 15 | 7 | 7 | 1 |
| 3. Workstations, utility | 20 | 17 | 0 | 3 |
| 4. Physical, direct | 29 | 13 | 0 | 16 |
| 5. Tool and item dependent | 24 | 12 | 1 | 11 |
| 6. Entity and environment | 24 | 7 | 12 | 5 |
| 7. Redstone | 33 | 27 | 3 | 3 |
| 8. Scheduled and world tick | 31 | 20 | 3 | 8 |
| 9. Special blocks | 18 | 8 | 1 | 9 |
| **Total** | **210** | **125** | **27** | **58** |

- **Works** means the behaviour the line names works. Many of these still lack a statistic, a game event (sculk
  vibration) or an advancement trigger. Phase 1 adds those for every interaction at once, so they don't count
  against the line.
- **Partial** means a visible part of the named behaviour is missing or wrong.
- **Missing** means no player or world path reaches it. Saved data, a menu id or a block state alone doesn't count.

The per-line status is in [the appendix](#appendix-a-status-of-every-line).

26.3's own block gameplay is part of this plan too, though the issue doesn't list it: shelves, straw beds, golden
dandelions and the poplar sapling. Apart from placing a straw bed, none of it works yet (see section 10 of the
appendix).

Two limits on these numbers. Parity is mostly unproven: only redstone (27 scenarios), fluids, daylight detectors,
block transformers, block item placement, note blocks and door use are replayed against captured vanilla output, and the rest has
hand-written tests or none. And a few vanilla details below come from memory rather than the jar; they're marked
"confirm by capture".

## Fix these first

Four defects found on the way are worse than a missing feature, because the block looks like it works.

1. **Fixed: ender chests lost items.** Opening one used to create a new empty container instead of the player's
   saved `EnderInventory`. Ender chests now open that saved inventory and retain its items across windows.
2. **Fixed: fire from fireballs, explosions and end crystals never spread or burned out.** Fire's first tick was
   scheduled by hand where a player lit it, where it spread and where lava lit it, and by nothing else. Fire now
   has vanilla's on-place behaviour (`OnPlaceBehaviors.FireBlock`), which schedules the tick for every placement
   and draws its delay once from the level's random. The hand-written scheduling in `FireBlockBehavior`,
   `AbstractLevel.Growth.cs` and `UseItemOnPacket` is gone. Hand ignition also leaves soul fire without a scheduled
   tick. The `fire_ticks.json` and `block_use.json` captures check fire and block-use behavior.
3. **Fixed: dispensers and droppers opened as a 9×1 row, and shulker boxes used the generic chest menu.**
   Dispensers and droppers now use their 3×3 menu; shulker boxes use their own menu and refuse nested shulker boxes.
4. **An observer facing a container pulsed forever.** Fixed: the block entity tick sent a neighbour update around
   every container every tick, and observers reacted to neighbour updates from the block they face. Containers now
   tell only comparators when their items change or a plugin consumes `SetChanged()` (performance item 1), and
   observers react through the generated shape owner shared by both update engines.
   Captured in `redstone_scenarios.json`.

Two more from the first audit were fixed by #695 before this plan started: unwaxed copper doors and trapdoors
now open by hand, and nether wart only plants on blocks that support it.

Also wrong, but less visible: turtle eggs hatch in a hard-coded window (15600 to 16560) where 26.3 reads
`gameplay/turtle_egg_hatch_chance` from the timeline (21062 to 21905). **Fixed: barrels never set their `open`
state.** It now follows their viewers.

## How blocks get their behaviour today

Eight mechanisms dispatch vanilla blocks' behaviour. New work should use the first.

| Mechanism | Covers | Bound by |
|---|---|---|
| Generated owner hooks (`BlocksGenerator.OwnedHooks`, `BlockOwnerBehaviors.cs`) | `randomTick`, `tick`, `updateShape`, `entityInside`, `onPlace` | registry id, from dumps of which vanilla class overrides what |
| `PlacementBehaviors` and `BlockPlacements` (#695) | `getStateForPlacement` for every block item | the vanilla class that declares it |
| `UseItemOnPacket.HandleAsync` | every right-click | a chain of about twenty `if` branches |
| `MainEventHandler.OnPlayerInteract` | menus | a `switch` on block id |
| `NeighborBehaviors` | redstone neighbour reactions | block name, then class name |
| `RedstoneBehaviors` | signals, redstone contact | class name |
| `ShapeUpdater.UpdateShape` | shape rules of about eighty classes | a string `switch` on class name |
| Hard-coded hazard checks (`PlayerBase.Food.cs`, `Mob.Environment.cs`, `Mob.Hazards.cs`) | cactus, magma, berry bushes, freezing | per entity class |

The owner hooks are the model: `VanillaDumper` records which Mojang class declares an override, the generator binds
each block to an Obsidian member named after that class, and a class without a member reports `OBSGEN001` and keeps
the old path. Adding a behaviour is then one class and one member, and the binding is a generated switch on the
block's id.

The table leaves out what isn't a vanilla block dispatcher but sits on the same paths, and must keep working:
plugins' block and item behaviours (`Plugins/BlockBehaviors.cs`, `Plugins/ItemBehaviors.cs`), block entity ticks,
scheduled-tick callbacks registered on `LevelBlockTicks` (which run before the generated tick owner), and the egg,
music and vibration state machines in `AbstractLevel.Eggs.cs`, `AbstractLevel.Music.cs` and
`AbstractLevel.GameEvents.cs`.

Three hooks are missing from that set, and most of the remaining checklist needs one of them:

- **use** (`useWithoutItem`, `useItemOn`, and `attack` for the dragon egg): sections 1 to 5 and 9.
- **stepped on** and **fallen on**: section 6.
- **neighbour changed**: section 7's consumers, which sit in the name-keyed dictionaries today.

## Ground rules

From `AGENTS.md`, as they apply here:

- **Reimplement, don't port.** Use the decompiled source for behaviour only. Port exactly only what clients, seeds
  or saves depend on (random draws in vanilla's order, constants, formulas), and say in a comment why.
- **Capture before implementing.** Each behaviour gets a vanilla capture and one replay test per fixture that loops
  over the cases and reports every failure at once. No hand-written tests of gameplay rules, and no `[Theory]` row
  per case.
- **Hand-written tests only for Obsidian-only risk.** Here that means item duplication or loss (ender chests,
  cauldron and bookshelf exchanges, hive harvests), saving and loading new block entity state, and lock order.
- **Run only the tests for what changed**, then `--filter "Category!=Worldgen"` once before committing.

Two capture routes already exist (`docs/scenario-captures.md`):

- **Java probes** registered in `FixtureDumper.cs` and run with `CaptureFixtures=<name>`. `MoreBlockFixtures.java`
  already calls `ServerPlayerGameMode.useItemOn` for note blocks. This is the route for single interactions: the
  starting blocks, the held item, the game mode and the click go in; block states, the hand, drops, sounds, level
  events, game events and statistics come out. The existing probe records fewer outputs than that: it discards
  statistics and game events (`MoreBlockFixtures.java:486` and `:708`), so recording them is part of adding the
  `block_use` fixture. A new fixture name also needs routing in `ScenarioCaptures.cs`, not only a method in
  `FixtureDumper.cs`.
- **RCON scenarios** on a headless server (`ScenarioCaptures.cs`), with a checkpoint per tick. This is the route for
  anything that plays out over ticks: redstone, growth, fire, entity contact.

Each phase below names the fixture it adds. Record only that fixture; don't regenerate the others.

## Phases

Phases 2 and 3 can run in parallel once phase 1 is in. Phases 4 to 6 are independent of each other. Sizes are rough:
S is a day or two for one agent, M about a week, L more than that and worth splitting.

### Phase 0: bring the record up to date (S)

Docs only. The issue itself is left as it is and closed when the whole plan is done; this document is the record
until then, so update the appendix as lines land.

- Correct the stale known-gaps files and code comments listed in [appendix B](#appendix-b-stale-documentation).
- Add a known-gaps file for each of the four defects above that isn't fixed straight away.

### Phase 1: one way to use a block (M)

The foundation for sections 1 to 5 and 9.

1. **Dump and bind use hooks.** Extend `VanillaDumper` to record which classes override `useWithoutItem`,
   `useItemOn` and `attack`. Add the three to `OwnedHooks` with a `UseBehaviors` class of members, and add each new
   class dump to `Assets.Get` (`Obsidian.SourceGenerators/Registry/Models/Assets.cs:31`), which imports only the
   five existing ones: a hook whose dump isn't imported gets a getter that never binds, with no diagnostic. The same
   goes for phase 3's contact hooks and phase 4's neighbour hook. A use behaviour
   returns a result with vanilla's shapes: success, success on the server only, consume, pass, fail, try with an
   empty hand. The result decides whether the item's own use runs next and whether the arm swings. Attacks don't
   arrive as uses: dispatch the `attack` hook from `PlayerActionPacket`'s start-digging path.
2. **One dispatcher in `UseItemOnPacket`.** In order: reach (`PlayerBase.BlockInteractionRange`, which only some
   branches check today), spectator and adventure rules, the sneak bypass (`BypassesBlockUse`, which the door, music
   and menu branches replace with a blanket sneak check), the block's use, the item's use on the block, placement,
   the acknowledgement.
   Adventure mode still runs block interactions and plugin item callbacks. Vanilla item uses and item placement
   are refused until Obsidian evaluates `can_place_on`, including where that predicate would permit them.
3. **One completion step** for what every interaction owes when it succeeds, driven by the result and by the
   behaviour: the `minecraft:used` statistic, custom statistics such as `interact_with_furnace` and `open_chest`,
   the `item_used_on_block` trigger, the game event, and the swing. This closes the "no statistic, no game event"
   gap that most of the working right-click lines share, and the swing known gap
   (`movement-and-swing-details.md`).
4. **Add the game events that don't exist**: `block_open`, `block_close`, eating, fluid pickup and place
   (`AbstractLevel.GameEvents.cs:7`). Emit `block_activate` and `block_deactivate` from levers, buttons and plates.
5. **Move the existing branches behind the hook one at a time**, doors first. Each move is checked by the existing
   replays (`VanillaMoreBlocks`, `DataSystems`, `VanillaBlockPlacement`). Placement itself was rewritten in #695
   and stays as it is; the dispatcher calls into it.
6. **Fix on the way**, since they sit in the code being moved: the ender chest inventory, the dispenser, dropper
   and shulker menus and shulker nesting, the barrel's `open` state.

Capture: a `block_use` Java probe fixture, starting with one case per existing interaction so that moving a branch
can't change it.

Performance alongside: items 2 (bind by id) and 7 (dispatch table).

### Phase 2: the right-click interactions that are missing (L, seven batches)

Each batch is a set of use behaviours plus the state they need. They don't share files beyond `UseBehaviors`, so
they can go to separate agents with one file per batch.

| Batch | Lines | What it needs beyond a use behaviour | Size |
|---|---|---|---|
| **A. Food and harvest** | cake, candle cake, sweet berry and cave vine harvest, pumpkin carving, vine trimming with shears, hive and nest honey and honeycomb, smoke from a campfire below | Hive harvest resets honey, angers or releases bees, and checks for smoke. Cave vine harvest needs the growth from phase 5. "Vines: trim" is ambiguous in the issue (appendix C): capture what shears do to a growing vine head and to wall vines, then implement what vanilla does or record that the line names nothing. | M |
| **B. Lighting** | candles and both campfires: light with flint and steel or a fire charge, put out by hand or shovel | Ignition today only places fire in the neighbouring cell. Projectiles lighting campfires waits for phase 3's projectile path. | S |
| **C. Cauldrons** | fill and empty with buckets and bottles, wash dyed items and banners | A table keyed by cauldron kind and item, as vanilla has. Exchanging the held item is where duplication bugs hide: hand-written tests for that are justified. Rain and dripstone filling come in phase 5. | M |
| **D. Blocks that hold items** | flower pots, chiseled bookshelves, decorated pots, lodestone compasses, jukebox comparator output | Bookshelves and pots become typed block entities with item access (hoppers) and analog output, instead of retained NBT. The bookshelf picks its slot from where the face was clicked. | M |
| **E. Copper waxing, bells, redstone ore** | wax with honeycomb, ring a bell, redstone ore lighting | Waxing isn't in `block_transformer.json`; capture how 26.3 routes it before assuming. Bells also ring from projectiles and redstone (phases 3 and 4) and alert villagers. Ore also lights when stepped on and goes dark on a random tick. | S |
| **F. Brushing and armor stands** | suspicious sand and gravel, armor stand equipment | Brushing is a held use over time aimed at a block: stroke cadence, progress that decays, loot, the final conversion. Armor stands are entity interactions at a clicked height, not block uses. | M |
| **G. 26.3 additions** | shelf storage, quick swapping and connections for every wood; straw bed sleep rules; golden dandelion interactions and age locking | Shelves are a typed block entity with item access and a comparator output, like batch D's. Straw beds already place (captured in `block_placement.json`); their sleep rules are missing: their own environment attribute, spawn behaviour and break on waking. Age locking is sent to clients already but never set or saved. `26-3-blocks.md` is the starting point; apart from straw bed placement nothing about these is captured yet, so capture comes first. | M |

Capture: more cases in the `block_use` fixture; batch F's brushing and batch G's sleep rules need RCON scenarios
because they run over ticks.

26.3's new entities (the Sulfur Cube and the cushion) and its mob changes aren't block interactions and aren't in
this plan. They're recorded as not implemented in `known-gaps/26-3-entities.md`.

### Phase 3: entities touching blocks (L)

The foundation for section 6, and performance item 3.

1. **One contact pass.** Replace `TouchPluginBlocksAsync`, the pressure plate pass and the tripwire pass with one
   pass per entity that reads each touched cell once and dispatches by a per-state flag. Add generated
   **stepped on** and **fallen on** hooks beside `entityInside`.
2. **Pose boxes for players.** Players have their standing box (0.6 by 1.8, generated from the entity data), and
   the contact pass, plates and tripwires all size entities from it. What's missing is the box changing with the
   pose: sneaking, swimming, gliding, sleeping. The comment at `PlayerBase.Food.cs:323` that says a player's
   dimension is zero is stale.
3. **Block speed and stuck-in-block in mob and item movement.** `EntityMovement` and `MovementControls` read only
   friction. Until they read the block's speed factor, jump factor and stuck multiplier, soul sand, honey, cobwebs,
   powder snow and berry bushes don't slow anything the server moves. Players are unaffected because the client
   moves them.
4. **Collision that depends on the entity**, for powder snow (leather boots) and scaffolding.
   `BlockCollisionShapes.Get` takes no entity.
5. **A projectile-hits-block path** used by every projectile. Only `Arrow.cs:461` calls
   `OnProjectileHitBlockAsync`. Spectral arrows and tridents inherit it; snowballs, eggs, potions, wind charges
   and the rest never reach it, so target blocks ignore them.

Then the lines, each a behaviour on one of the hooks:

- **Fallen on:** farmland trampling, turtle eggs, and fall damage on slime, hay, honey and beds for mobs (mobs take
  full fall damage on slime today), plus the slime bounce for mobs and items.
- **Stepped on:** magma (with the `hot_floor` damage type and frost walker immunity), turtle eggs (per tick, not per
  step event, and not while sneaking), redstone ore, big dripleaf tilting with its scheduled ticks, the shrieker.
- **Inside:** cobweb, sweet berry bush (not at age 0, with its own damage type), powder snow (sinking, putting out
  fire, melting under burning entities), cactus (its real shape, destroying items), lit campfires (missing
  entirely), honey sliding, pressure plates, tripwire, the hopper's item pickup.
- **Scaffolding:** falling when unsupported and the entity-dependent collision. #695 already places it with its
  distance and bottom state; extending a tower by clicking it is in `known-gaps/block-item-placement.md`.
- **Bubble columns started by water:** give water the hooks `bubble-columns-started-by-water.md` describes and
  re-enable the two skipped tests.
- **Sculk:** more event sources (redstone components, doors, containers, pistons, eating, fluids, explosions), the
  calibrated sensor's filter, timers as scheduled block ticks so they survive a save, warning levels saved per
  player.
- **Plates and tripwire details:** armor stands on stone plates, the contact box, weighted plates re-reading only on
  their 10-tick check, hook sounds, shears disarming.

Capture: a `block_contact` RCON scenario fixture: an entity dropped, pushed or walked onto each block, with position,
velocity, health and block state per tick. Plate and tripwire timing must be captured before the pass moves, since
that changes when they fire.

### Phase 4: redstone leftovers (M)

Section 7 is 28 of 33 working. What's left:

- **Dispenser item behaviours** (the largest piece). Only boats and minecarts dispense; everything else drops as an
  item. Add a registry keyed by item, called from `RedstoneDispenser.Fire`: projectiles, fire charges, buckets,
  bone meal, flint and steel, TNT, shears, armor, shulker boxes, pumpkins and skulls, bottles, honeycomb, spawn
  eggs, fireworks, glowstone into anchors. Many reuse phase 2's behaviours (cauldrons, hives, waxing), so this
  follows phase 2.
- **Trapped chests** give power from their viewer count (`AbstractLevel.ContainerLids.cs:179` already counts).
- **Comparator outputs that read 0:** jukebox, chiseled bookshelf, decorated pot, copper bulb, sculk sensors,
  command block, creaking heart, copper golem statue, shelf. Cats on chests don't block the reading.
- **Calibrated sculk sensors** read the signal on their back to filter vibrations.
- **Bells** ring from redstone. **Lightning rods** give power.
- **Sounds and events:** piston contract sound, copper bulb sounds, slime blocks launching entities off pistons.
- **Move the consumers onto a generated neighbour-changed hook**, off the name-keyed dictionaries.

Capture: more scenarios in `redstone_scenarios.json`. Doors, TNT, rails, crafters and droppers work but have no
capture; add one each while here. Hoppers pulling from a chest are captured (`hopper_feeds_comparator`), pushing
isn't. Scenarios marked `level` replay on a whole test level, for blocks that need block entities.

Performance alongside: item 4 (typed properties for dust, repeaters, comparators). Item 1 is done. Items 5 and 6
are their own PRs after this phase.

### Shape rules by declaring class (M, its own PR)

`ShapeUpdater.UpdateShape` picks a block's shape rule from its concrete vanilla class, so every subclass has to be
listed by hand: `WeatheringCopperDoorBlock` beside `DoorBlock`, `StrawBedBlock` beside `BedBlock`, and about thirty
plant classes that all inherit one rule. A subclass nobody lists silently gets no rule, which is the bug #695 fixed
for placement. Vanilla dispatches by the class that *declares* `updateShape`, and the generated asset
`Assets/block_update_shape_classes.json` already records that for every block whose class overrides it: 108
declaring classes over 854 blocks when this was written, where the switch has 29 arms. The generator binds 18 of them
through `ShapeUpdateBehaviors` today.

Tags are not the key to use. Vanilla chooses the rule by class, a datapack can change a tag without changing a
block's shape logic, and most of these rules (torches, ladders, lanterns, vines, dust, double plants) have no tag.
Tags stay where vanilla reads them, inside the rules: what a fence or wall connects to.

1. **Key the existing rules by declaring class.** Each arm of the switch becomes the rule of the Mojang class that
   declares it (`DoorBlock`, `AbstractBedBlock`, `LanternBlock`, `VegetationBlock`, ...), bound by the generator
   like the 18 owners that exist. The hand-listed subclasses go, and so do their `BlockClassId` members where
   nothing else uses them (copper weathering reads `WeatheringCopperDoorBlock`, for one).
2. **Keep worldgen working.** Trees, structure templates and terrain call `ShapeUpdater` through `IWorldGenLevel`,
   while the generated owners take the live `IBlockLevel`. The rules need one read-only view both can supply; shape
   rules return a state and schedule ticks, they don't write.
3. **List what the dump has and Obsidian doesn't.** The dump names declaring classes with no rule yet: slabs,
   flower pots, candles and candle cakes, pressure plates, coral, chains, lightning rods, shelves, amethyst
   clusters and more. Don't implement them here; record each in appendix A or a known-gaps file with the phase
   that owns it.

No behaviour change is intended for blocks that have a rule today, so the existing replays guard it
(`VanillaMoreBlocks` and `VanillaSulfurBlocks` shape cases, `VanillaBlockPlacement`, `VanillaRedstone`, the worldgen
feature and structure captures). Where a subclass gains a rule it was missing, capture it first.

The observer fix has landed (#703) and moved the observer onto the same generated hook, so this can start. That
work also showed a gap this refactor should close: structure templates and worldgen call `ShapeUpdater` directly,
which never dispatches a generated owner's `UpdateShape`, so an observer placed by a template gets no shape
reaction. Worldgen reaches owners only through post-processing (`IPostProcessBehavior`, which soul sand and magma
implement). The shared view in step 2 is where that is fixed.

### Phase 5: ticking (L)

Section 8 is 20 of 31 working. The pattern for most of what's left is one `RandomTickBehaviors` or `TickBehaviors`
member per Mojang class; the generator binds it.

- **Copper weathering (done):** all 13 `Weathering*` owners share the jar-derived age/next-stage table.
  `copper_weathering.json` captures every family and stage, draw order, neighborhood effects and connected halves.
- **Growing plants:** kelp, cave vines (with glow berries), weeping and twisting vines (`GrowingPlantHeadBlock`),
  melon and pumpkin stems, vines, the pitcher crop, the poplar sapling (needs weighted tree growers, see
  `saplings-that-26-3-added.md`).
- **Melting and weather:** ice, frosted ice, snow layers, and vanilla's `tickPrecipitation` in
  `AbstractLevel.TickChunkBlocks` (snow building up, water freezing, cauldrons filling in rain).
- **Pointed dripstone:** growth, dripping, cauldron filling, falling. `SulfurSpikeBehavior` owns the class but
  returns early for anything that isn't a sulfur spike.
- **Budding amethyst, sculk catalysts** (spreading on mob deaths), **nylium, eyeblossoms, dried ghasts, coral
  dying, dirt paths, chorus plant survival, zombified piglins from portals.**
- **Fire (done):** an on-place behaviour that schedules the first tick, and the draws in vanilla's order, checked
  by the `fire_ticks.json` probe fixture.
- **Turtle eggs** from the timeline and the `#sand` tag; **frogspawn** that hatches however it was placed.
- **Furnaces:** unlock the recipe and fire `recipe_unlocked` when the result is taken.

One thing to know before starting: a block that vanilla random-ticks but Obsidian has no behaviour for makes no
draws, and neither does the missing weather pass, so the level's random sequence parts from vanilla's wherever those
would draw. Captures of growth draws only match once the blocks ticked before them in the same chunk are done.
Capture each behaviour through a Java probe with its own seeded random rather than through a whole-level scenario.

Capture: a `random_ticks` Java probe fixture (state in, draws consumed and state out), and an RCON scenario for
dripstone. Fire turned out to fit a Java probe (`fire_ticks.json`), so it has no RCON scenario.

Performance alongside: items 8 (tick only block entities that tick) and 4 (typed properties for leaves and crops).

### Phase 6: special blocks and trial chambers (L)

- **Chest and ender chest opening rules:** a chest refuses to open under a solid block or a sitting cat, for both
  halves of a double chest; an ender chest refuses under a solid block. Ender chest sounds.
- **Shulker lids** follow their own rule: the lid needs room in the direction the box faces, not above it. While
  open, the box grows its collision box and pushes entities (the piston's entity pushing in
  `MovingPistonBlockEntity.cs:135` is the model). Confirm both rules by capture.
- **Dragon egg** teleporting on use and on attack. **Conduits** as a typed block entity. **End gateways:** the
  beam, and the safe-exit search when `ExactTeleport` is false.
- **Trial spawners and vaults.** Both are untyped saved data today. Each is a typed block entity with a state
  machine: player detection, waves scaled by player count, tracked mobs, cooldown, ominous trials; keys,
  per-player reward tracking, ejection. This is the largest single piece in the issue: plan it as several PRs
  (typed block entities and saving, the spawner's state machine, the vault, ominous trials).
- **Command blocks:** send the op permission level to clients (entity events 24 to 28), without which the client
  won't open the editor; handle `SetCommandBlock`; tick impulse, chain and repeating blocks; trigger from redstone.
  `CommandBlockMinecart` already runs commands and can share the executor.
- **Structure and jigsaw blocks:** editors, save, load and generate. They need the same op permission level as
  command blocks, so they follow them.

Capture: `block_use` cases for the opening rules and the dragon egg; RCON scenarios for trial spawners and vaults.

## Order and parallel work

1. Phase 0 and the four fixes. The fixes don't need phase 1 and shouldn't wait for it.
2. Phase 1, with performance items 2 and 7.
3. Phases 2 and 3 side by side. Phase 2's seven batches are independent of each other.
4. Phase 4, then performance items 5 and 6 on their own. The shape-rule refactor can run beside any phase once
   the observer fix has merged. It adds members to `BlockOwnerBehaviors.cs`, which other phases edit too, and
   otherwise shares files mainly with performance item 6.
5. Phases 5 and 6 in either order.

For delegated batches, `AGENTS.md` allows two agents per model family at once. Give each batch its own behaviour file
and its own fixture file, and keep `UseBehaviors`, `BlockOwnerBehaviors.cs` and `BlocksGenerator` edits to one line
per batch so merges stay trivial.

## Decisions (maintainer, 2026-11-09)

- **26.3's new block gameplay is in scope:** shelves, straw beds, golden dandelions and the poplar sapling (phase 2
  batch G and phase 5).
- **Trial spawners and vaults stay in this plan**, in phase 6. They're large, not separate.
- **Command, structure and jigsaw blocks are in scope**, in phase 6.
- **Performance items 5 and 6 go ahead**, and it's fine that they change what Obsidian does today. Both move it
  towards what vanilla does (block changes sent once per tick; one depth-first updater), so the bar is the same as
  for any parity change: capture first, then replay.
- **26.3's new entities are out of this plan** and recorded in `known-gaps/26-3-entities.md`.
- **The issue is left alone** and closed once the whole plan is implemented.

## Appendix A: status of every line

File paths are under `Obsidian/`. "Feedback" means statistics, game events or advancement triggers that phase 1
adds.

### 1. Doors, switches and mechanical blocks

| Line | Status | What's left |
|---|---|---|
| Wooden and copper doors and trapdoors, fence gates: open and close | Works | Captured (`block_use`) |
| Levers | Works | Captured |
| Buttons | Works | Press captured; the release isn't |
| Note blocks | Works | Custom player-head sounds |
| Repeaters, comparators, daylight detectors: toggle by hand | Works | Daylight detector's adventure rule unconfirmed |
| Redstone ore lights up | Missing | Use and step-on; the random tick that turns lit ore off works (`ticking_blocks.json`) |
| Bells ring | Missing | No activation at all; the block event queue already carries bell events |
| Crafters toggle slots | Works | |
| Iron doors, iron trapdoors respond to redstone | Works | Feedback; no capture |

### 2. Containers

| Line | Status | What's left |
|---|---|---|
| Chests, double chests | Works | Obstruction is section 9's line; lock enforcement |
| Trapped chests | Partial | No redstone signal from viewers |
| Ender chests | Works | Captured |
| Barrels | Works | Captured |
| Shulker boxes | Partial | No lid room check |
| Hoppers | Works | |
| Dispensers, droppers | Works | Captured |
| Furnaces, blast furnaces, smokers, brewing stands | Works | |
| Chiseled bookshelves | Missing | No use handler; saved books only drop on break |
| Decorated pots | Partial | Break and save work; no insertion, wobble, hopper access or comparator |

### 3. Workstations and utility blocks

| Line | Status | What's left |
|---|---|---|
| Crafting tables | Works | |
| Enchanting tables | Works | No sound; offers not captured |
| Anvils (all three), grindstones, stonecutters | Works | Results not captured |
| Smithing tables | Works | Trim material from a fixed list, not `provides_trim_material` |
| Looms | Works | Built-in patterns only |
| Cartography tables | Works | Capacity check stricter than vanilla |
| Lecterns: books and pages | Works | |
| Beacons | Works | No `construct_beacon`; pyramid measured at once |
| Jukeboxes | Works | Comparator reads 0 |
| End portal frames | Works | Portal-opening sound sent locally, not globally |
| Respawn anchors: charge, set respawn | Works | Explodes next to water, unlike vanilla; no set-spawn sound |
| Lodestones | Missing | The tracker component exists; nothing binds a compass |
| Vaults, trial spawners | Missing | Untyped saved data only |

### 4. Physical and direct interactions

| Line | Status | What's left |
|---|---|---|
| Beds: sleep, set respawn, explode | Works | `sleep_in_bed` statistic; stand-up position order |
| Cake, candle cakes | Missing | Only the comparator output exists |
| Candles: light and extinguish | Missing | Only water and wind charges put them out |
| Campfires, soul campfires: cook | Works | Feedback |
| Campfires, soul campfires: light and extinguish | Missing | Only water and wind charges put them out |
| Composters: insert, collect | Works | Feedback; thrown items aren't taken |
| Cauldrons: buckets, bottles, washing | Missing | Only the comparator output exists |
| Flower pots | Missing | Planting and taking plants; potted eyeblossoms turn with the time of day (`eyeblossom_ghast_portal_ticks.json`) |
| Item frames, glow item frames | Works | |
| Armor stands | Missing | Poses only |
| Signs, hanging signs: edit | Works | The edit reservation stores no side or expiry, and another player can take it |
| Signs: ink, glow ink, dye | Works | Dyes found by item name, not the dye component |
| Signs: wax | Works | Vanilla has no way to remove sign wax (confirm) |
| Beehives, bee nests: honey, honeycomb | Missing | Honey accumulates; nothing harvests it |
| Suspicious sand, suspicious gravel | Missing | Loot data is there; no brushing |

### 5. Tool and item dependent interactions

| Line | Status | What's left |
|---|---|---|
| Logs, wood: strip | Works | Captured (`DataSystems`) |
| Copper: scrape, remove wax | Works | Captured |
| Copper: apply wax | Missing | Not in `Assets/block_transformer.json` |
| Copper bulbs: wax and scrape | Partial | Scraping works, waxing doesn't |
| Grass to path; dirt, coarse dirt, rooted dirt with a hoe | Works | Captured, with drops |
| Pumpkins: carve | Missing | |
| Beehives, bee nests: shears | Missing | |
| Campfires: shovel; candles and campfires: flint and steel | Missing | |
| Respawn anchors: glowstone | Works | Feedback |
| End portal frames: eye | Works | |
| Farmland: plant crops; soul sand: nether wart | Works | Captured (`block_placement.json`) |
| Sweet berry bushes, cave vines: harvest | Missing | Foxes harvest berries; players can't |
| Vines: trim with shears | Missing | |
| Beehives: smoke | Missing | |

### 6. Entity and environment interactions

| Line | Status | What's left |
|---|---|---|
| Pressure plates, weighted plates | Works | Fire after entities tick, not as they move; armor stands don't press stone plates; contact box |
| Tripwire, tripwire hooks | Works | No hook sounds; shears don't disarm |
| Farmland: trample | Missing | |
| Turtle eggs: trample | Partial | Rolled per step event, not per tick; sneaking doesn't protect |
| Slime blocks: bounce | Partial | Mobs and items don't bounce, and mobs take full fall damage |
| Honey blocks: slow | Partial | Nothing slows mobs or items |
| Honey blocks: slide | Missing | |
| Magma blocks | Works | `in_fire` damage type instead of `hot_floor`; frost walker |
| Soul sand, cobwebs | Partial | Nothing slows mobs, items or arrows |
| Powder snow: sink | Partial | No leather boots, stuck-in-block slowdown or fire interaction (mobs do slow as they freeze) |
| Powder snow: freezing | Works | Judged from the feet block only |
| Bubble columns | Partial | Pushing works; water doesn't start a column, and two tests are skipped |
| Big dripleaf | Missing | |
| Scaffolding | Missing | Placement distance exists (#695); no falling, no climbing collision |
| Sweet berry bushes | Partial | Hurts at age 0; no slowdown; generic damage type |
| Cactus | Works | Melee damage kind; items aren't destroyed |
| Campfires: damage | Missing | |
| Sculk sensors | Partial | Few event sources; timers not saved; no sound or particles |
| Sculk shriekers: react, summon | Partial | Warning levels not saved; darkness timing (confirm by capture) |
| Target blocks | Partial | Arrows, spectral arrows and tridents only |

### 7. Redstone and reactive behaviour

| Line | Status | What's left |
|---|---|---|
| Dust, torches, burnout, repeaters (delay, lock), comparators (compare, subtract) | Works | Captured (`redstone_scenarios.json`) |
| Comparators: read containers | Works | Captured, through conductors and double chests too. Several blocks read 0 (phase 4); unopened loot containers read stored contents on purpose |
| Observers | Works | Captured: containers, state changes without neighbour updates, command placement, piston push/pull completion, and player placement facing air or stone (no initial tick) |
| Pistons, sticky pistons | Works | No contract sound; slime doesn't launch entities; captured for the basic cases only |
| Redstone lamps | Works | |
| Copper bulbs | Works | No sounds; comparator reads 0 |
| Dispensers | Partial | Only boats and minecarts dispense |
| Droppers, hoppers (transfer, lock), crafters | Works | No capture |
| Note blocks on redstone | Works | |
| Bells from redstone | Missing | |
| Doors, trapdoors, fence gates, TNT | Works | Feedback; no capture |
| Rails: shape, powered, activator, detector | Works | No capture |
| Daylight detectors | Works | Captured |
| Lecterns: page signal | Works | |
| Chiseled bookshelves: signal | Missing | |
| Sculk sensors: signal | Partial | Comparator reads 0; timers not saved |
| Calibrated sculk sensors | Missing | Accept every vibration |

### 8. Scheduled and world-tick behaviours

| Line | Status | What's left |
|---|---|---|
| Furnaces, blast furnaces, smokers | Works | No recipe unlock on taking the result |
| Brewing stands, campfires | Works | |
| Crops | Works | Pitcher crop, melon and pumpkin stems don't grow |
| Saplings | Works | Poplar doesn't grow |
| Bamboo, sugar cane, cactus, nether wart, cocoa, sweet berries | Works | No capture of growth draws |
| Cave vines | Missing | Kelp and the nether vines too (`GrowingPlantHeadBlock`) |
| Grass, mycelium: spread | Works | |
| Copper: oxidize | Works | Captured random draws, all 13 owners, connected doors/chests and retained block entities |
| Leaves: decay | Works | |
| Ice, snow layers: melt | Missing | No weather pass either |
| Fire | Works | Captured (`fire_ticks.json`). Fire lights a portal later in the tick than vanilla's; soul fire placed off a soul fire base isn't put out on placement (`BaseFireBlock.onPlace` is unbound); fire a shape update destroys goes later in the tick |
| Lava, water | Works | Flow is captured; lava's ignition isn't |
| Farmland: hydrate | Works | |
| Turtle eggs: hatch | Works | |
| Frogspawn | Works | Frogspawn placed by `/place` or a live feature never hatches (no on-place) |
| Eyeblossoms: open and close | Works | Captured (`eyeblossom_ghast_portal_ticks.json`); bees aren't poisoned ([gaps](known-gaps/eyeblossoms-dried-ghasts-and-portal-piglins.md)) |
| Dried ghasts: rehydrate, become ghastlings | Works | Captured (`eyeblossom_ghast_portal_ticks.json`); buckets don't waterlog them, no placement sound |
| Sculk catalysts, pointed dripstone, amethyst buds | Partial | Pointed dripstone and amethyst buds work (`ticking_blocks.json`); mud-to-clay and path-to-dirt do not lift entities; sculk catalysts missing |
| Trial spawners, vaults | Missing | |

### 9. Special block interactions

| Line | Status | What's left |
|---|---|---|
| Command blocks (all three), structure blocks, jigsaw blocks | Missing | Packets generated with no handler; clients aren't sent the op level |
| Spawners: spawn eggs | Works | Trial spawners aren't handled |
| Dragon eggs | Missing | Only falling |
| End gateways | Works | No beam; no safe-exit search |
| Nether portals, end portals | Works | Zombified piglins spawn from portals (`eyeblossom_ghast_portal_ticks.json`); the floor rule isn't vanilla's ([gaps](known-gaps/eyeblossoms-dried-ghasts-and-portal-piglins.md)); portal travel has no capture |
| Conduits | Missing | |
| Beacons: effects | Works | |
| Enchanting tables: animation; bookshelves: power | Works | The animation is the client's |
| Ender chests: animation | Partial | Lid moves; no sound |
| Shulker boxes: push entities | Missing | |
| Chests: refuse when obstructed | Missing | |
| Chests: lid by viewers | Works | Captured |

### 10. 26.3 additions (not on the issue's list)

| Line | Status | What's left |
|---|---|---|
| Shelves: storage, quick swapping, connections | Missing | Placement only, for every wood; comparator reads 0 |
| Straw beds: placement, sleep rules | Partial | Placement works and is captured; its sleep rules don't exist (environment attribute, spawn behaviour, break on waking) |
| Golden dandelions: interactions, age locking | Missing | The lock is sent to clients but never set or saved |
| Poplar saplings: grow | Missing | Needs weighted tree growers (`saplings-that-26-3-added.md`) |

## Appendix B: stale documentation

Statements the code contradicts, to correct in phase 0.

- **`docs/known-gaps/redstone.md`:** "the rest of vanilla's redstone doesn't [work]"; the consumers list (only
  bells is still true); the sources list (only trapped chests and lightning rods are still true); "detector rails
  read 0".
- **`docs/known-gaps/plant-growth-beyond-saplings.md`:** "only saplings and the propagule do anything", "crops don't
  grow on random ticks", "leaves don't decay", and its "Where to start". Still
  true: ice, snow, copper, weather, bone meal on other plants.
- **`docs/known-gaps/vanilla-blocks-reactions-to-block-updates.md`:** doors, trapdoors, rails and TNT "aren't
  modelled"; scheduled ticks of other vanilla blocks "do nothing"; "only pressure plates and wooden buttons react to
  entities inside them"; its "Where to start".
- **`docs/known-gaps/item-access-beyond-containers.md`:** "dispensers drop every item" (boats and minecarts
  dispense); minecarts with chests and hoppers (both work now); "Where to start" names
  `DispenserComponent.Fire` in `ItemMovers.cs`, which doesn't exist (it's `RedstoneDispenser.Fire`).
- **`docs/known-gaps/26-3-gameplay-left-out-of-the-port.md`:** lists leaf distance as uncaptured; `more_blocks.json`
  has it.
- **`docs/plugin-content.md:285`:** "other powered blocks (doors, pistons and the like) don't react yet".
- **Code comments:** `AbstractLevel.Redstone.cs:93` ("comparators don't read containers yet"); the
  `PressurePlates.cs` class remarks ("before entities tick"); `FarmlandBlockBehavior.cs:16` (says trampling waits for
  a landing hook, which now exists for turtle eggs).

## Appendix C: not settled by the audit

To confirm by capture or by running a server before the work that depends on it:

- Whether powder snow's recorded collision shape lets mobs fall through.
- Whether a bubble column's motion packet also reaches the pushed player, doubling the push on their client.
- Vanilla values quoted from memory: the pressure plate touch box, the calibrated sensor's active time, the
  shrieker's darkness and cooldown rules.
- Whether survival players may change a spawner with a spawn egg in 26.3, and whether creative mode consumes eyes of
  ender.
- What the issue means by "vines trim" (stopping a growing vine head, or shearing wall vines). Honeycomb waxing is
  settled: `HoneycombItem.useOn` (batch E).
