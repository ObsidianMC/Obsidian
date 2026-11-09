# Mob ambient sounds


**Status:** open.

Vanilla mobs make their ambient sound now and then (`Mob.playAmbientSound`, from `getAmbientSound` per mob class),
and once as a spawn egg spawns them (`EntityType.spawn`). Obsidian's mobs make none.

`VanillaBlockUse` therefore leaves the ambient sound out of its spawn egg case (`spawn_egg_on_stone` in
`block_use.json`), which records `minecraft:entity.sheep.ambient`.

**Where to start:** give `Mob` an ambient sound per mob type, with vanilla's interval (`getAmbientSoundInterval` and
the `ambientSoundTime` countdown), play it from `UseItemOnPacket.UseSpawnEggAsync`, and drop the exception from
`VanillaBlockUse`.
