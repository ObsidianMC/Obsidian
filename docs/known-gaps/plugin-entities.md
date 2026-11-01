# Plugin entities


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
