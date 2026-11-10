# Dispenser behaviours

**Status:** open. Dispensers fire items through a registry keyed by item (`DispenseBehaviors`, in
`Obsidian/WorldData/Dispensing`), like vanilla's `DispenserBlock.DISPENSER_REGISTRY`, with vanilla's defaults for
equipment and spawn eggs and the drop for everything else. `dispenser.json` (`VanillaDispensers`) captures each
behaviour from vanilla tick by tick; see [scenario captures](../scenario-captures.md). What follows is what still
differs or isn't there yet.

## Still dropped

These items have a behaviour in vanilla but are dropped like any other here:

- **Buckets** of every kind, filled and empty, were left for a follow-up on #720, which changed how a result item
  goes back into a container.
- **Glass bottles** (water and full beehives) and **honey bottles** wait for phase 2's cauldrons and hives, and
  **water potions** turning dirt into mud for the same bottle hand-back.
- **Honeycomb** was left for a follow-up on #725's waxing.
- **Wind charges**: Obsidian has no flight for a wind charge outside the breeze's. The capture keeps vanilla's shot
  (`wind_charge`, pinned).
- **Lingering potions**: Obsidian throws no lingering potion yet.
- **Armor stands** (placed as an entity), **chests** onto tamed donkeys, mules and llamas, and **brushes** on
  armadillos.

Droppers are unchanged: they still put an item into the container they face or drop it.

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
- **Landing.** A mob that appears standing on a floor (a dispensed spawn egg's, the cow a sheared mooshroom turns into)
  is heard landing (`hit_ground`) on its first tick in Obsidian; vanilla's emits nothing, as it hasn't fallen
  (`pig_spawn_egg` and `shears_mooshroom`, pinned). This is Obsidian's landing game event, not the dispenser's.

## Atomicity and locks

A dispenser snapshots its selected stack under its inventory lock outside the tick, transfer and chunk locks,
then releases it before preparing a shulker box's components or waiting for tick. The commit takes tick first and
only **tries** the inventory. It revalidates the resident dispenser, inventory revision and exact selected stack,
then commits the slot and synchronous world/entity state together. A busy or changed inventory refuses the attempt
without spending or firing. There is no inventory-to-tick wait edge. Block notifications and spawn effects use the
same deferred effects scope as `BlockUse.TryCommit`, publishing after the inventory unwinds; loot, chunk loading,
placement factories and asynchronous preparation stay outside the commit. Growth features also run in the
post-inventory effects phase, and plugin shearables/equipment callbacks are not invoked inside the commit.

`DispenserAtomicity.FiringAndRegionSaveCannotDeadlockEachOther` starts `DoWorldTickAsync` with firing waiting behind
a held click, pauses a real `Region.FlushAsync` inside its tick-locked snapshot, then releases the click. The old
firing path times out with both operations unfinished; the fixed path saves and spends exactly one glowstone to
charge the anchor. The uninterruptible regression runs in an isolated testhost, which is killed and joined if it
hangs. This does not depend on the save-side repair in #719. The other `DispenserAtomicity` tests guard clicks and
concurrent hopper-like takes against item duplication/loss.

## Replay comparisons

The four known-gap cases pin exact values for the affected fields and still compare every other captured value.
They cannot pass merely because the same field names differ. The mooshroom/cow grounding gap also pins its exact
vertical gravity accumulator (`-0.0784` instead of vanilla's zero).

Cases with `details: true` additionally compare complete item component patches in slots, projectile pickup/items,
equipment and dropped stacks, shulker contents, rocket angled state, deterministic sound category/volume/pitch and
position, particle positions, mob velocities and vertical TNT velocity. Dropped component payloads are summed by
identical stack rather than entity count. Projectile motion is recorded: the initial velocity comparison allows
only the two shots' spread radius; following ticks must have the captured drag and vertical acceleration. Exact
random flight coordinates, TNT's random horizontal impulse, sound seeds and the arrow hit's random pitch are not
compared. Only selected added/changed cases are re-recorded with `CaptureCases`.

**Where to start:** a new behaviour is an `IDispenseBehavior` registered in one of `DispenseBehaviors`'s partial
files (`Projectiles`, `Blocks`, `Entities`), with a case added to `dispenser.json` and captured on its own.
