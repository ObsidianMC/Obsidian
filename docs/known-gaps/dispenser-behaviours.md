# Dispenser behaviours

**Status:** open. Dispensers fire items through a registry keyed by item (`DispenseBehaviors`, in
`Obsidian/WorldData/Dispensing`), like vanilla's `DispenserBlock.DISPENSER_REGISTRY`, with vanilla's defaults for
equipment and spawn eggs and the drop for everything else. `dispenser.json` (`VanillaDispensers`) captures each
behaviour from vanilla tick by tick; see [scenario captures](../scenario-captures.md). What follows is what still
differs or isn't there yet.

## Still dropped

These items have a behaviour in vanilla but are dropped like any other here:

- **Buckets** of every kind, filled and empty, wait for #720, which changes how a result item goes back into a
  container.
- **Glass bottles** (water and full beehives) and **honey bottles** wait for phase 2's cauldrons and hives, and
  **water potions** turning dirt into mud for the same bottle hand-back.
- **Honeycomb** waits for #725's waxing.
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
  `projectile_shoot` game event is emitted by the dispenser where the projectile will be after its first move, with
  no source; vanilla's projectile emits it on its first tick. Projectiles nothing shot (a dispenser's, or ones loaded
  from a save) no longer emit one when they spawn.
- **Bone meal** grows what a player's bone meal grows here (see [plant growth](plant-growth-beyond-saplings.md)); cocoa
  isn't one of them (`bone_meal_cocoa`, pinned), and water plants (vanilla's `growWaterPlant`: seagrass and coral
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

A dispenser holds its inventory while it fires (`ContainerBlockEntity.LockInventoryAsync`), waiting for it on the
level's entity path, outside the tick, transfer and chunk locks. The item it spends and what it does happen together:
a click or a hopper can't take the item meanwhile, and it isn't spent unless the behaviour ran. Under the lock it
changes the level, which takes the tick lock after the inventory, as the lock order allows; any other inventory it
needs it only tries. `DispenserAtomicity` checks both.

**Where to start:** a new behaviour is an `IDispenseBehavior` registered in one of `DispenseBehaviors`'s partial
files (`Projectiles`, `Blocks`, `Entities`), with a case added to `dispenser.json` and captured on its own.
