# Comparators reading unopened loot containers


**Status:** deliberately narrowed to stored inventory contents.

Vanilla 1.21.11's `AbstractContainerMenu.getRedstoneSignalFromContainer` reads slots via `getItem`, and
`RandomizableContainerBlockEntity.getItem` calls `RandomizableContainer.unpackLootTable(null)`. Obsidian's
comparators instead read stored contents without rolling pending loot. Loot functions such as exploration maps
can synchronously load structure chunks; doing that under the level's tick lock can deadlock chunk publication.
Opening a container still rolls its loot and notifies the comparators of its changed contents.

**Where to start:** prepare loot outside the tick lock, then validate the resident container and publish the result
under the lock. Do not restore synchronous unpacking inside a comparator read.
