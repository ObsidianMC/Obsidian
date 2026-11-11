# Dispenser behaviours

**Status:** open. Dispensers fire items through a registry keyed by item (`DispenseBehaviors`, in
`Obsidian/WorldData/Dispensing`), like vanilla's `DispenserBlock.DISPENSER_REGISTRY`, with vanilla's defaults for
equipment and spawn eggs and the drop for everything else. `dispenser.json` (`VanillaDispensers`) captures each
behaviour from vanilla tick by tick; see [scenario captures](../scenario-captures.md). What follows is what still
differs or isn't there yet.

## Still dropped

These items have a behaviour in vanilla but are dropped like any other here:

- **Glass bottles at full beehives** wait for hive harvesting (#731), which isn't merged on this base.
- **Sulfur cube buckets** still drop their item; their mob placement is outside this phase's fluid, fish and
  axolotl bucket scope.
- **Wind charges**: Obsidian has no flight for a wind charge outside the breeze's. The capture keeps vanilla's shot
  (`wind_charge`, pinned).
- **Lingering potions**: Obsidian throws no lingering potion yet.
- **Armor stands** (placed as an entity), **chests** onto tamed donkeys, mules and llamas, and **brushes** on
  armadillos.

Droppers are unchanged: they still put an item into the container they face or drop it.

## Buckets, bottles and waxing

Water, lava and powder snow buckets place their contents; fish, axolotl and tadpole buckets also spawn their mob,
with saved bucket data, species components and the item's custom name. Species components take precedence over
older variant NBT. Empty buckets pick up sources, waterlogged blocks and powder
snow. Glass bottles fill from water, including flowing and waterlogged water, without removing it. Water potions
turn blocks in `convertible_to_mud` into mud, and honeycomb waxes the copper block in front with its properties.

A result replaces the selected slot when its last item is used. With a custom stack size on a filled bucket or
water potion, only one item is consumed and the other originals stay in that slot. When originals remain, vanilla's
`addToInventory` path scans slots in order, using an empty slot or merging a compatible stack with room, and
dispenses the result when neither fits. Components and maximum stack sizes are respected when merging.
The captures include those paths and vanilla's extra click/smoke events for nested drop fallbacks. Cauldrons are
unchanged: vanilla dispensers don't fill or empty them with buckets, bottles or water potions.

Powder snow buckets place only into air; supported grass and other occupied blocks cause the filled bucket to
drop. A failed fluid/block write, including an unavailable target chunk, keeps the filled bucket, with no
empty-bucket remainder or mob spawned. Pouring into an identical fluid source or already-waterlogged container
still succeeds.

## Differences in what is there

- **Projectiles.** The spread of each shot is drawn from Obsidian's random, where vanilla draws it from the new
  projectile's own, so positions aren't compared, only which projectiles fly and when. A tipped arrow flies as a plain
  arrow carrying the tipped arrow to pick up: its potion's effects aren't applied, as with a bow. The
  `projectile_shoot` game event is emitted on the projectile's first tick, through the shared projectile path,
  with no source for a dispenser's shot. An arrow hitting the wall on that tick emits `projectile_land` at the hit
  block before `projectile_shoot` at its retreated position (`review_arrow_hits_wall`).
- **Bone meal** grows what a player's bone meal grows here (see [plant growth](plant-growth-beyond-saplings.md)); cocoa
  isn't one of them (`bone_meal_cocoa`, pinned to keeping both bone meal), and water plants (vanilla's `growWaterPlant`: seagrass and coral
  spreading on water) aren't grown either, so the dispenser fails on those and keeps the bone meal.
- **Flint and steel and TNT** don't prime sulfur cubes or feed TNT to one, and fire is placed only where it survives,
  without vanilla's exception for the inside of a nether portal frame.
- **Shears** don't cut leads, and harvesting a full beehive doesn't release the bees inside it.
- **Equipment** goes onto mobs only: players and armor stands in front aren't equipped. A mob takes equipment while it
  picks up loot, and a horse a saddle or body armour once tamed (donkeys and mules a saddle only grown); the other
  mobs' own rules (pigs, striders and happy ghasts' saddles and harnesses, wolves' and nautiluses' armour) aren't
  modelled.
- **Shulker boxes** are placed even where an entity stands in the way.
- **Golems and withers.** A carved pumpkin is placed only on top of two snow blocks, standing: iron and copper golems
  aren't built, and vanilla also builds a golem lying on its side. A pumpkin or skull with nothing to complete is put
  on a mob's head instead, as in vanilla.
- **Spawn eggs** spawn the egg's mob only: its other entity data and the stack's custom name aren't applied.
- **A grounded no-AI mob's motion.** The cow a sheared mooshroom turns into keeps a downward motion of -0.0784 in
  Obsidian where vanilla's is zero (`shears_mooshroom`, pinned).
- **Bucketed axolotl ambience.** The captured `bucket_mob_axolotl` emits `entity.axolotl.idle_air` after the empty-bucket
  sound at tick 4. Obsidian emits only the bucket sound; that exact sound list is pinned.
- **Powder snow contact particles.** The dropped bucket touching powder snow in
  `powder_snow_bucket_into_powder_snow` emits one snowflake in vanilla at tick 4. Obsidian omits it; the particle and
  its exact captured position (`1.503360,1.000000,0.546411`) are pinned, with the drop and events still compared.
- **Potion component serialization.** Vanilla writes the default `show_icon:1b` on the speed effect in
  `potion_custom_effect`; Obsidian omits it. The complete slot/drop component values are pinned from tick 1,
  including the overflow count suffix. Both keep the custom-effect water potion from making mud.

## Atomicity and locks

A dispenser snapshots its selected stack under its inventory lock outside the tick, transfer and chunk locks,
then releases it before preparing a shulker box's components, bucket loot or mob data, or waiting for tick.
Bucket preparation snapshots the target and resident block entity under tick, then evaluates loot and reads
components outside both locks. The commit revalidates that target as well as the inventory. It takes tick first and
only **tries** the inventory. It revalidates the resident dispenser, inventory revision and exact selected stack,
then commits the slot and synchronous world/entity state together. A busy or changed inventory refuses the attempt
without spending or firing. There is no inventory-to-tick wait edge. Block notifications and spawn effects use the
same deferred effects scope as `BlockUse.TryCommit`, publishing after the inventory unwinds; loot, chunk loading,
placement factories and asynchronous preparation stay outside the commit. Growth features also run in the
post-inventory effects phase, and plugin shearables/equipment callbacks are not invoked inside the commit.

`DispenserAtomicity.BusyCommitKeepsTheBucketAndRetryDeliversItsResultOnce` holds tick until an observed inventory
snapshot lets a click acquire inventory, then releases tick while keeping the click held. Firing must refuse the
busy inventory without changing the source or the stack; a retry delivers one result. Explicit signals and gates
force this interleaving, without sleeps or timeouts. The other tests guard a click taking the selected arrow and
bottle conservation across selected-slot, empty-slot and overflow delivery.
`FailedBucketWriteKeepsTheFilledItemWithoutSpawningItsContents` uses a resident dispenser at a chunk edge with
its neighbour unavailable from the start. It checks item conservation for water, lava, powder snow and an axolotl
bucket without relying on an unload race.

## Replay comparisons

The known-gap cases pin exact values for the affected fields and still compare every other captured value.
They cannot pass merely because the same field names differ. The mooshroom/cow grounding gap also pins its exact
vertical gravity accumulator (`-0.0784` instead of vanilla's zero).

Cases with `details: true` additionally compare complete item component patches in slots, projectile pickup/items,
equipment and dropped stacks, shulker contents, rocket angled state, deterministic sound category/volume/pitch and
position, particle positions, mob velocities and vertical TNT velocity. Dropped component payloads are summed by
identical stack rather than entity count. Bucket data cases also compare the mob's health, NoAI, gravity flag and
custom name. The phase's original 60 additions and 10 review regressions compare from their first tick; seeded
cases pin slot selection and splash positions. The review cases cover custom stacked buckets/potions, compatible
and incompatible remainder stacks, overflow, occupied powder-snow targets, and axolotl/tropical-fish variant
components. Their `variants: true` opt-in compares the mob's variant metadata too. The original 59 cases are unchanged.
Projectile motion is recorded: the initial velocity comparison allows
only the two shots' spread radius; following ticks must have the captured drag and vertical acceleration. Exact
random flight coordinates, TNT's random horizontal impulse, sound seeds and the arrow hit's random pitch are not
compared. Only selected added/changed cases are re-recorded with `CaptureCases`.

**Where to start:** a new behaviour is an `IDispenseBehavior` registered in one of `DispenseBehaviors`'s partial
files (`Projectiles`, `Blocks`, `Entities`, `Fluids`), with a case added to `dispenser.json` and captured on its own.
