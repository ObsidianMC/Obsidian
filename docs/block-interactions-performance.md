# Block interaction performance

Performance work for the code that issue #538 (block interaction tracking) touches: using a block, writing a block
and updating its neighbours, redstone, entities touching blocks, and block entity ticks. It goes with the
[block interactions plan](block-interactions-plan.md), which says which phase each item rides in. World generation,
mob AI and general networking are out of scope.

Written 2026-11-09 against `26.3.x` at `cd79d4dd6`. Line numbers were re-checked at `066dd1f01`.

## What was measured

A throwaway probe (not committed) ran in Release on .NET 10 against `RedstoneTestWorld`: real `Chunk`s with the live
`BlockNotificationPipeline` and the redstone engine installed as `AbstractLevel` installs them, but no level, players,
packets or second update engine. Each row is one run on a desktop that wasn't idle, so read the numbers as sizes, not
as benchmarks.

| Operation | Time | Allocated |
|---|---|---|
| Per-state flag read (`IsRedstoneConductor`) | 9 ns | 0 |
| Behaviour bound by block id (`GetTickBehavior`) | 4 ns | 0 |
| `BlockClass()` | 19 ns | 0 |
| `IsGravityAffected()` (two `BlockClass()` calls) | 35 ns | 0 |
| `GetProperty("waterlogged")` on a block without it | 25 ns | 0 |
| `int.Parse(GetProperty("power"))` | 41 ns | 0 |
| `WithProperty("power", 7)` | 45 ns | 0 |
| `NeighborBehaviors.TryGet` on stone (no behaviour) | 19 ns | 0 |
| `RedstoneBehaviors.For` on stone | 16 ns | 0 |
| `RedstoneBehaviors.ContactFor` on air | 17 ns | 0 |
| One block change inside solid stone, through the pipeline | 1.76 µs | 32 B |
| Toggling a redstone block that feeds 15 dust on stone | 255 µs | 18.3 KB |
| The same with 30 dust (the signal still reaches 15) | 263 µs | 18.5 KB |

Three things follow from it.

- **Lookups by name cost more than a lookup by id; the multiplier varies.** This probe measured 16 to 45 ns for
  name/class lookups against 4 ns for generated per-id bindings. The warmed baseline below measured `BlockClass()`
  at 8.43 ns against 3.78 ns for id-bound tick dispatch, about 2.2 times as much.
- **A block change that nothing reacts to still costs about 1.8 µs** in the pipeline alone: six shape steps and six
  neighbour steps, each reading two blocks and looking for a behaviour that isn't there.
- **Redstone dust is the expensive case.** One toggle of a 15-dust line is about 255 µs. Vanilla's dust algorithm
  changes each wire several times as power steps down, and that part must stay (observers and clients see the
  steps), so the cost per step is what can come down. As a rough projection, 100 such lines toggling in one tick
  would spend half the 50 ms budget in the pipeline.

Not measured: the real level's write path (the second update engine, block entity upkeep, heightmaps, packets), the
entity contact passes and the block entity tick. Items below that rest on reading the code say so. For context,
PR #677 measured 40 roaming bots at 40 to 43 ms per tick, with about 17 ms in entity ticks.

Random ticks are already tuned (PR #677 checks vanilla's per-state `isRandomlyTicking` table before resolving a
block), so nothing here revisits them.

## Proposals

Ordered by how much they should save for how little risk. "Measured" means the probe above backs the size;
"from the code" means it doesn't yet.

### 1. Stop notifying comparators around every container on every tick

*Done. The defect below was real: an observer facing an idle chest pulsed every four ticks.*

The revision gate covers item changes. Consumed plugin `SetChanged()` calls also notify comparators, since a
container may expose saved machine state through `GetAnalogOutput` without changing slots. The inventory revision
and the plugin's `SetChanged()` generation are acknowledged only after delivery; notification failures and bounded
drain drops leave either kind of change pending for the next block entity tick. Item access, the block entity pass
and save consumption skip the same already-delivered pair, keeping the earliest notification without extra drains.

*From the code. Small change, no behaviour change intended.*

`AbstractLevel.TickBlockEntitiesAsync` (`Obsidian/WorldData/AbstractLevel.BlockEntities.cs:262`) calls
`UpdateComparatorsOf` for every container block entity in block-ticking range on every tick, changed or not. Each
call takes the level's tick lock, queues six neighbour steps and drains the pipeline. Going by the probe's cost per
step, that is roughly 1 µs per chest, barrel, hopper, furnace or shulker box per tick, whether or not a comparator
is anywhere near.

It also looks like a bug: observers react to a neighbour update from the block they face
(`Redstone/RedstoneObserver.cs:43`), so an observer facing a container would be told every tick and pulse without
stopping. That is from reading the code, not from running it.

Notify only when the container's `Inventory.Revision` differs from the revision last notified, as the lines just
above already do for plugin containers. Notify the way vanilla's `updateNeighbourForOutputSignal` does, which
`NotifyComparatorsAroundAsync` already implements: the comparators beside the container and those behind a
conductor beside it. Today's six plain neighbour updates never reach a comparator behind a conductor. A double chest must notify for both halves when either changes, since a
comparator beside one half reads the combined inventory. This also removes most of the known gap's "comparators hear
on the next block entity tick" polling.

Rides with: plan phase 4 (redstone), where comparator timing is touched anyway.

### 2. Bind neighbour, redstone and contact behaviours by block id

*Measured. Mechanical, no behaviour change.*

Four dispatchers look blocks up by string today:

- `NeighborBehaviors` (`BlockNotifications/NeighborBehaviors.cs`): three dictionaries keyed by block name, then one
  keyed by class name. A block with no behaviour, which is nearly every block, pays for the name lookup, a
  `BlockClass()` lookup and the class lookup.
- `RedstoneBehaviors.For` and `ContactFor` (`Redstone/IRedstoneBehavior.cs`): keyed by class name, called for every
  neighbour of every signal read.
- `BlockPhysics.BlockClass`, `IsSignalSource` and `BlockEntityType` (`Registries/BlockPhysics.cs`): dictionaries and
  a set keyed by `UnlocalizedName`.
- `ShapeUpdater.UpdateShape` (`Features/Tree/ShapeUpdater.cs:90`): a string switch over about eighty class names, run
  for each neighbour of each changed block.

Resolve all of them into arrays indexed by registry id when they're registered or loaded: a class registration
expands to every block of that class, `BlockClass` becomes a generated integer per block (the strings stay for logs
and plugins), and `IsSignalSource` becomes a boolean array read by registry id. `ShapeUpdater` switches on
the integer. Plugin blocks keep their own path, which already tests the state id first.

This is the same shape as the generated owner bindings (`BlocksGenerator.OwnedHooks`: `GetTickBehavior`,
`GetShapeUpdateBehavior`, `GetEntityInsideBehavior`), so the plan's new hooks should be added there rather than to
the string dictionaries.

Rides with: plan phase 1, before the new behaviours are added, so they're bound the cheap way from the start.

### 3. One pass for entities touching blocks

*From the code. Changes when plates and tripwires fire, towards vanilla's timing.*

Three separate passes walk the blocks each entity touches. The first runs straight after each entity's own tick;
the other two run once every entity has ticked:

- `TouchPluginBlocksAsync` (`AbstractLevel.BlockUpdates.cs:39`), per entity: every block its box overlaps, then the
  block it stands on. Each cell reads the block, then asks `RedstoneRules.ContactFor` (a class-name lookup) and
  `BlockBehaviors.HasBehavior`, through an async call per cell. Most cells are air.
- `PressurePlates.UpdateAsync` (`Redstone/PressurePlates.cs:99`), over a fresh entity snapshot: puts every entity's
  footprint into two dictionaries and two hash sets, then reads the block under each cell to see whether it's a
  plate. This runs in full when the level has no plates.
- `TripWire.UpdateAsync` (`Redstone/TripWire.cs:250`): reads every cell of every entity's box again, looking for wire.

Replace them with one pass that reads each touched cell once and checks a per-state "reacts to contact" flag before
doing anything else. Run it per entity, straight after that entity's tick, as the first pass does today and as
vanilla does: a single pass at the end of the entity loop would change the order in which entities affect each
other. A cell without the flag costs one array read. Cells with it go to an id-bound contact
behaviour: plates, tripwire, detector rails and bubble columns today, and the section 6 blocks the plan adds
(cobweb, sweet berry bush, powder snow, cactus, magma, campfire, honey, slime, big dripleaf, turtle eggs, farmland).
Without the flag those additions would each add a lookup per entity per cell per tick.

Vanilla presses plates and trips wires from `entityInside` as the entity moves, which the redstone known gap lists
as a timing difference, so this pass is also where that gets closer. Capture the plate and tripwire timing from
vanilla before moving it; the existing redstone scenario replays guard the rest.

Rides with: plan phase 3 (entity contact). It is the phase's foundation, not an extra.

### 4. Typed block-state properties on the hot paths

*Measured. No behaviour change, but touches many call sites if done widely, so do it narrowly.*

`GetProperty(name)` finds the state's property dictionary, then the value by name, and returns a string that the
caller compares or parses. `WithProperty(name, value)` looks up a `(state, property, value)` tuple in a concurrent
dictionary, hashing both strings. Redstone dust reads `power` with `int.Parse` and writes it back through
`WithProperty` on every step.

Vanilla's state ids are contiguous per block, one stride per property, and the generator already knows each block's
base id and properties. Add property handles resolved once (`power`, `powered`, `facing`, `open`, `age`,
`waterlogged`, `enabled`, `lit`, `distance`, `level`) whose get and set are integer arithmetic on the state id, and
use them where the probe or a profile shows the cost: `RedstoneWire`, repeaters and comparators, `Hoppers.TickAsync`
(`enabled` and `facing` every tick per hopper), `ShapeUpdater.ScheduleFluidTick` (`waterlogged` for every neighbour
of every change), leaves and crops.

Don't convert the roughly 1,600 by-name call sites. The by-name API stays for data-driven code and plugins.

Rides with: plan phases 4 and 5, for the components each batch touches.

### 5. Send block changes to clients once per tick per section

*From the code. Changes packet timing to vanilla's, so it needs a check on a real client. Approved 2026-11-09.*

`LevelFluids.OnBlockStored` broadcasts a `BlockUpdatePacket` for every block write as it happens, and
`PlayersWithChunk` walks every player in the level for each one. Vanilla collects changed positions per chunk
section and sends them at the end of the tick: one block update, or one section update listing each changed block's
final state. Obsidian can't send that packet yet: `SectionBlocksUpdatePacket` is a generated shell with an id and no
fields, because the packet generator skips packed and repeated fields (`Packets/Encodings.cs:47`), so it writes
nothing. Its fields and a packed serializer are the first step of this item.

Depowering a 15-dust line writes on the order of a hundred intermediate states. Obsidian sends each to every player
in range; vanilla sends fifteen final states in one packet. Fluids spreading, pistons, explosions and falling
leaves have the same shape.

Collect changes per section during the tick and flush once, after block events. Two things must move with it:

- **Acknowledgements.** `UseItemOnPacket` and block breaking send `BlockChangedAck` at once. If the block updates
  wait for the end of the tick and the ack doesn't, clients drop their prediction, show the old block, then get the
  new one. Vanilla sends acks from the connection tick, after the level's broadcasts. Acks need to queue behind the
  flush.
- **Block events.** `docs/known-gaps/26-3-blocks.md` records that event packets are sent immediately, in order with
  piston block updates. Vanilla sends events during the tick and block changes at its end, so the client-visible
  order changes; check pistons and note blocks on a real client.

Rides with: its own PR after plan phase 4. Worth doing before large redstone builds are a target.

### 6. One update engine per block change

*From the code. The largest structural item. It moves update order to vanilla's, which can change what redstone
does today, so it needs order-sensitive captures first. Approved 2026-11-09.*

A level block write that changes the state and asks for updates runs two engines in turn
(`AbstractLevel.AfterBlockSet`):

1. `FluidLevel.OnChanged` with `Fluids/NeighborUpdater.cs`, which mirrors vanilla's collecting neighbour updater:
   six `neighborChanged` calls for liquids, falling blocks and plugin hooks, then six shape updates.
2. `BlockNotificationPipeline`: six shape steps and six neighbour steps again, for the redstone behaviours.

Both run shape rules for the same six neighbours, so such a change evaluates up to twelve shape updates where
vanilla evaluates six, and schedules each fluid tick twice (the scheduler drops the duplicate). The first engine
also allocates seven small objects per change. The two don't dispatch identically: both end in
`ShapeUpdater.UpdateShape`, but the first also asks plugins, signs, placement rules and falling blocks
(`BlockUpdates.cs:211`), while the pipeline only asks the generated shape owners first
(`AbstractLevel.BlockNotifications.cs:51`). Writes that skip updates don't pay any of this: the default public
`SetBlockAsync` sets `KnownShape`, unchanged states return early, and shapes and neighbours each follow their own
flag.

The two engines order work differently. The first is depth-first like vanilla. The pipeline is first-in, first-out
with pending duplicates coalesced and a 10,000-step limit, and redstone only runs in the pipeline. So redstone
components and plugin or fluid reactions can't interleave the way vanilla's single updater does.

Suggested order:

1. Add counters (shape evaluations, neighbour steps and block writes per tick) to confirm the duplication on a live
   level.
2. Drop the pipeline's shape steps on the live level, where the first engine has already run them, and check the
   redstone and block scenario replays.
3. Decide whether redstone moves onto the vanilla-order updater. That changes update order, so it needs redstone
   scenario captures that pin order-sensitive contraptions first.

Rides with: nothing. Its own PRs, after plan phase 4 so that the new redstone consumers are covered by captures.

### 7. A dispatch table for using a block

*From the code. Affects click latency and lock contention, not tick time.*

`UseItemOnPacket.HandleAsync` tries about twenty interactions in order for every right-click: fish buckets, music
blocks, campfires, plugin hooks, composters, redstone, beds, respawn anchors, doors, signs, TNT, fences, hanging
entities, menus, minecarts, spawn eggs, portals and fire, bone meal, block transformers. Placing a plain block walks
all of them first. Several take the level's tick lock just to find the
block isn't theirs (`UseCampfireAsync`, `UseRedstoneAsync`, `VanillaPlacement.Use`), and placement then takes it
several more times. Each acquisition waits for whatever the tick is doing under that lock.

The plan's phase 1 replaces the chain with use behaviours bound by block id: one block read, one lookup, at most
one lock section for the block's use, then the item's. It also gives one place to check reach
(`PlayerBase.BlockInteractionRange`), which only some handlers check today.

Two small things to fix on the way:

- `ItemUseOnBehaviors.Evaluate` walks `block_transformer.json` as `JsonElement`s on every click, with LINQ and string
  comparisons, under the tick lock. Compile the transformers once into block-id sets and target states.
- `ItemUseOnBehaviors.Component` asks `CombatItems.Component` for every item used on a block, tools or not.

Rides with: plan phase 1.

### 8. Tick only the block entities that tick

*Done. The pass is about 15 percent faster, not twice as fast; what is left is the upkeep walk below.*

The pass still walks each block-ticking chunk's block entity dictionary once, in its order, exactly where the old
loop did. What changed is what it does per entry. A reflection check, made once per class, tells whether the class
overrides `TickAsync` (`BlockEntity.Ticks`), so a new class that ticks can't be left out. One that ticks is checked
against its block, placed, loaded and ticked as before. One that doesn't is first asked whether it has anything to
do: not placed yet, an `OnLoad` pending, or a container whose items or comparators are behind. Only then is it
checked against its block and kept up. Chests, signs, beds and banners with nothing to do no longer pay for a block
lookup, a type comparison and an awaited tick that does nothing. Each container's revision, which scans every slot,
is read once per pass.

A first version kept a per-chunk array of the ticking block entities, marked stale by the chunk's block entity
methods, and kept up the others in a second walk after each chunk's ticks. It halved the pass, but review found three
problems, all from the cached array, so it wasn't kept:

- A removed plugin block entity stayed in the array of a chunk that emptied or left block-ticking range, keeping its
  plugin's assembly from unloading.
- A block entity a plugin put into the public `Chunk.BlockEntities` dictionary directly was never ticked, since no
  chunk method marked the array stale.
- Upkeep after the ticks changed what comparators were told. A comparator computes its input when it hears of a
  change, before deciding to schedule its update. So a chest filled outside the tick and emptied by a hopper later in
  the same pass no longer scheduled the update it used to.

Same harness as the re-runnable block probe below: 25 block-ticking chunks holding 3,000 chests and 1,000 signs,
plus an idle furnace and an idle hopper in each. One operation is one call of `TickBlockEntitiesAsync`. All runs were
made on 2026-11-09 on the machine described there, on that day's 26.3.x; ranges are over two to four runs.

| Version | ns/op | B/op |
|---|---:|---:|
| Before this change | 201,000 to 204,500 | 18,384 |
| Cached array of tickers, upkeep after them (not kept) | 103,400 to 107,100 | 16,784 |
| One walk inside the async pass (this change) | 168,300 to 180,300 | 18,384 |
| One walk, outside the async pass until a tick waits (not kept) | 139,400 to 144,700 | 18,384 |

The single walk costs more than the cached array because of where it runs, not what it does. Inside the async pass
the dictionary's enumerator lives across the awaited ticks, so it stays a heap object reached through interface
calls. The same idle work in a plain synchronous `foreach` cost about 94 µs instead of about 162 µs. There .NET 10
keeps the enumerator on the stack and calls it directly; that is what the cached array's separate upkeep loop got
for free. Keeping the walk synchronous until a tick actually waits, and handing the enumerator to an async
continuation then, recovers about 25 µs. It wasn't kept: it needs a second copy of the walk, and the enumerator it
hands off still can't stay on the stack.

What remains is the upkeep walk itself: enumerating every block entity of every block-ticking chunk, and reading
every container's revision, each tick. Only making that upkeep event-driven removes it: block entities placed when
their chunk loads, and containers that report their own changes.

*From the code.*

`TickBlockEntitiesAsync` walks every block entity of every block-ticking chunk each tick. For each it reads the
block, looks up its block entity type by block name, compares it to the block entity's id ignoring case, and awaits
`TickAsync`, including for chests, signs, beds and banners, which do nothing. Vanilla keeps a list of tickers per
chunk and only walks that.

The same loop also does upkeep that isn't ticking. For block entities of every type, in every generated chunk
whether or not it is block-ticking, it restores kept ones (`RestoreKeptBlockEntities`), attaches them and runs
their pending loads. For plugin containers it also polls the inventory revision, so that changes made without the
inventory's lock still reach `OnItemsChanged` and get saved. All of that must keep running on its own path,
whatever the block entity's type and whether or not it ticks.

Keep a per-chunk list of block entities whose type ticks, maintained where block entities are added and removed,
and validate the block against the block entity when either changes rather than every tick.

Rides with: plan phase 5 (furnaces, brewing, campfires and the other ticking block entities).

### 9. Smaller items

- **Pipeline bookkeeping.** `BlockNotificationPipeline` takes its own lock for every enqueue and dequeue, makes an
  async call per step, and dedupes steps in a hash set of five-field records. Check the block's behaviour flag
  before queueing a neighbour step's work, and keep the per-step path synchronous when the behaviour completes
  synchronously. Fold into item 2 or 6. Keep the pipeline's own lock: not every caller holds the tick lock.
  `NotifyNeighborsAsync` (`AbstractLevel.BlockNotifications.cs:76`) enqueues and drains without it, and pressure
  plates and lever and button use reach it that way. Removing the lock needs those callers serialized first.
- **Hoppers.** Besides the property reads in item 4, each transfer resolves item access for the hopper and its
  targets again, allocating a combined access for double chests. Measure a hopper line before changing anything:
  the timings contraptions depend on are easy to break.

## Measuring before and after

None of the level-side items above has a number yet. Before starting on them:

- Split the `fluids` lap of `TickTimings` into block ticks and fluid ticks, and add counters to the timing window:
  block writes, shape evaluations, neighbour steps, contact cells and block update packets per tick.
- Give `Obsidian.LoadTest` a world with redstone clocks, dust lines, hopper chains and a chest room, so the laps
  mean something for this area. The roaming bots of PR #677 exercise none of it.
- Keep the probe's cases somewhere they can be re-run. `Obsidian.WorldgenBench` already sees the server's internals;
  a block mode there avoids a new project.

Every item should land with its before and after from the same harness, and with the non-worldgen suite green, as
`AGENTS.md` asks.

## Re-runnable block probe

```sh
dotnet build Obsidian.WorldgenBench -c Release -m:1
dotnet run --project Obsidian.WorldgenBench -c Release --no-build -- block > block.csv
```

The probe shares `RedstoneTestWorld`, runs offline, excludes setup, and reports the median of seven samples after
250 ms warmup per case; one dust operation is an alternating on/off transition.
Before: docs branch point `15309c34b`; both runs: 2026-11-09, AMD Ryzen 9 9950X, Windows 11 (10.0.22631), x64,
.NET SDK 10.0.403, runtime .NET 10.0.14, Release. Shared-machine timings include load and JIT variation.

| Operation | Before (ns/op) | After (ns/op) | Before (B/op) | After (B/op) |
|---|---:|---:|---:|---:|
| BlockClass / stone | 8.43 | 2.66 | 0.00 | 0.00 |
| IsSignalSource / stone | 6.05 | 1.72 | 0.00 | 0.00 |
| IsSignalSource / wire | 13.06 | 3.37 | 0.00 | 0.00 |
| IsGravityAffected / stone | 18.94 | 2.30 | 0.00 | 0.00 |
| NeighborBehaviors.TryGet / stone | 13.16 | 2.43 | 0.00 | 0.00 |
| NeighborBehaviors.TryGet / wire | 7.24 | 5.21 | 0.00 | 0.00 |
| RedstoneBehaviors.For / stone | 12.84 | 2.84 | 0.00 | 0.00 |
| RedstoneBehaviors.For / wire | 20.32 | 4.49 | 0.00 | 0.00 |
| ContactFor / air | 15.66 | 2.56 | 0.00 | 0.00 |
| BlockEntityType / chest | 7.45 | 2.32 | 0.00 | 0.00 |
| UpdateShape / stone | 18.27 | 11.59 | 0.00 | 0.00 |
| UpdateShape / air | 26.74 | 19.25 | 0.00 | 0.00 |
| Pipeline / change inside solid stone | 1131.65 | 833.63 | 32.05 | 32.05 |
| Pipeline / toggle 15 dust | 275583.20 | 185805.40 | 18293.95 | 18293.95 |
| Pipeline / toggle 30 dust | 267843.20 | 188184.60 | 18493.95 | 18493.95 |

Vanilla neighbor, placement, removal, redstone and contact dispatch now binds to arrays by registry id.
Class and block entity metadata also bind by id, shape/gravity dispatch uses integer classes, and `IsSignalSource()`
reads a per-block boolean. Named APIs, block-specific precedence and dynamic plugin registration/unloading remain intact.
