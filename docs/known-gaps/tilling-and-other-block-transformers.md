# Tilling and other block transformers


**Status:** implemented for vanilla 26.3's transformers.

`UseItemOnPacket` runs the stack's `block_transformer` component after block and plugin interactions. Hoes till,
axes strip, scrape and remove wax, and shovels make paths. The generated registry supplies predicates, states,
sounds, particles, durability and rooted-dirt loot. Component overrides and removals work on other items too.
`DataSystemsFixtures.java` captures vanilla's provider results; `DataSystems` replays them and exercises packet use.

Gameplay tag uses were checked against 26.3's tags. Mushroom survival, and the worldgen helpers that named the
removed `mushroom_grow_block` tag, now use `overrides_mushroom_light_requirement`.
